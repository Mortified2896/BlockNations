"""Balanced frozen-policy evaluation, separate from PPO and changing self-play Elo.

Uses the actual C# worker and fair seat observations. Every policy plays both
colours and both starting positions. Turn limits are reported as interruptions.
Reference roster restrictions are probes, never rules imposed on a learner.
"""
import argparse
from collections import Counter
import json
import math
from pathlib import Path
import time
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionTuple

from dotnet_environment import DotNetEnvironment, NAMES, SPEC
from frozen_policy import FrozenPolicy, SAMPLING_MODES, evaluation_weights
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION


def wilson(wins, games):
    if not games:
        return None
    z = 1.96
    p = wins / games
    centre = (p + z*z / (2*games)) / (1 + z*z/games)
    margin = z * math.sqrt(p*(1-p)/games + z*z/(4*games*games)) / (1 + z*z/games)
    return [max(0, centre-margin), min(1, centre+margin)]


class FrozenActor(FrozenPolicy):
    def __init__(self, checkpoint, network):
        super().__init__(checkpoint, network, SPEC)


class TacticalActor:
    """A frozen rules-based reference, never an omniscient controller."""
    sha256 = None

    def __init__(self, work=128, recruit_type=''):
        self.work, self.recruit_type = work, recruit_type
        self.name = 'fair-hard-tactician-v3'

    def choose(self, decisions, random, difficulty, *, environment, seat):
        advice = environment.reference_actions(seat, self.recruit_type, self.work)
        return np.array([[advice[int(agent)]] for agent in decisions.agent_id], dtype=np.int32)


def evaluate(worker, directory, candidate, reference=None, *, games_per_cell=16, seed=10001,
             reference_type='', reference_work=64, difficulty='Hard', reference_difficulty=None):
    if games_per_cell < 4 or games_per_cell % 4:
        raise ValueError('Games per cell must be a positive multiple of four.')
    reference_difficulty = reference_difficulty or difficulty
    if difficulty not in SAMPLING_MODES or reference_difficulty not in SAMPLING_MODES:
        raise ValueError('Choose a supported policy sampling mode.')
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=False)
    started = time.monotonic()
    records = []
    reference = reference or TacticalActor(reference_work, reference_type)
    for first in (0, 1):
        for candidate_seat in (0, 1):
            cell = directory / f'first-{first}-candidate-{candidate_seat}'
            cell.mkdir()
            env = DotNetEnvironment(worker, cell, 4, seed, 7, False, 2, uuid.uuid4().hex,
                                   opening_economy_version=OPENING_ECONOMY_VERSION, first_seat=first)
            # Independent seeded streams for each colour, unaffected by Torch RNG.
            randoms = [np.random.default_rng(seed + seat*7919) for seat in (0, 1)]
            finished = [0]*4
            recruits = [[Counter(), Counter()] for _ in range(4)]
            quota = games_per_cell // 4
            try:
                env.reset()
                while min(finished) < quota:
                    _, terminals = env.get_steps(NAMES[candidate_seat])
                    for index, agent in enumerate(terminals.agent_id):
                        arena = int(agent)//2
                        if finished[arena] < quota:
                            interrupted = bool(terminals.interrupted[index])
                            reward = float(terminals.reward[index])
                            records.append(dict(candidateSeat=candidate_seat, firstSeat=first, worker=arena,
                                game=finished[arena], interrupted=interrupted,
                                winner=None if interrupted else candidate_seat if reward > 0 else 1-candidate_seat,
                                round=env.stats[arena]['round'],
                                candidateRecruits=dict(recruits[arena][candidate_seat]),
                                referenceRecruits=dict(recruits[arena][1-candidate_seat])))
                            finished[arena] += 1
                        recruits[arena] = [Counter(), Counter()]
                    if min(finished) >= quota:
                        break
                    for seat, name in enumerate(NAMES):
                        decisions, _ = env.get_steps(name)
                        if not len(decisions):
                            continue
                        actor = candidate if seat == candidate_seat else reference
                        mode = difficulty if seat == candidate_seat else reference_difficulty
                        actions = actor.choose(decisions, randoms[seat], mode, environment=env, seat=seat)
                        for agent, action in zip(decisions.agent_id, actions[:, 0]):
                            if 242 <= action < 258:
                                recruits[int(agent)//2][seat][env.roster[int(action)-242]] += 1
                        env.set_actions(name, ActionTuple(discrete=actions))
                    env.step()
            finally:
                env.close()
    captures = [game for game in records if not game['interrupted']]
    first_wins = sum(g['winner'] == g['firstSeat'] for g in captures)
    wins = sum(g['winner'] == g['candidateSeat'] for g in captures)
    breakdown = []
    for starts in (True, False):
        games = [g for g in records if (g['candidateSeat'] == g['firstSeat']) == starts]
        won = sum(not g['interrupted'] and g['winner'] == g['candidateSeat'] for g in games)
        limits = sum(g['interrupted'] for g in games)
        breakdown.append(dict(candidateStarts=starts, games=len(games), wins=won,
                              losses=len(games)-won-limits, interruptions=limits))
    recruit_counts, winning_recruits = Counter(), Counter()
    for game in records:
        recruit_counts.update(game['candidateRecruits'])
        if not game['interrupted'] and game['winner'] == game['candidateSeat']:
            winning_recruits.update(game['candidateRecruits'])
    result = dict(version=1, rulesVersion=RULES_VERSION, boardSize=7, openingEconomyVersion=OPENING_ECONOMY_VERSION,
        candidate=candidate.name, candidateSha256=candidate.sha256,
        reference=reference.name, referenceSha256=reference.sha256,
        referenceRecruitType=getattr(reference,'recruit_type',None), referenceWork=getattr(reference,'work',None),
        candidateRecruitType=getattr(candidate,'recruit_type',None), candidateWork=getattr(candidate,'work',None),
        seed=seed, difficulty=difficulty, referenceDifficulty=reference_difficulty,
        games=len(records), captures=len(captures), wins=wins,
        losses=len(captures)-wins, interruptions=len(records)-len(captures),
        candidateCaptureWinRate=wins/len(captures) if captures else None,
        candidateWinRate95=wilson(wins, len(captures)),
        firstPlayerCaptureWinRate=first_wins/len(captures) if captures else None,
        firstPlayerWinRate95=wilson(first_wins, len(captures)), startingPositionResults=breakdown,
        candidateRecruits=dict(recruit_counts), candidateWinningRecruits=dict(winning_recruits),
        elapsedSeconds=time.monotonic()-started, records=records)
    (directory/'evaluation.json').write_text(json.dumps(result, indent=2)+'\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', type=Path, required=True)
    candidate_kind = parser.add_mutually_exclusive_group(required=True)
    candidate_kind.add_argument('--checkpoint', type=Path)
    candidate_kind.add_argument('--tactical-candidate', action='store_true', help='Measure a fixed fair tactical policy in both starting positions.')
    parser.add_argument('--trainer-config', type=Path)
    parser.add_argument('--destination', type=Path, required=True)
    parser.add_argument('--reference-checkpoint', type=Path)
    parser.add_argument('--reference-recruit-type', default='')
    parser.add_argument('--reference-work', type=int, default=64)
    parser.add_argument('--games-per-cell', type=int, default=16)
    parser.add_argument('--seed', type=int, default=10001)
    parser.add_argument('--difficulty', choices=SAMPLING_MODES, default='Hard',
                        help='Policy samples the learned distribution without difficulty adjustments.')
    parser.add_argument('--reference-difficulty', choices=SAMPLING_MODES,
                        help='Defaults to the candidate sampling mode; set explicitly to compare presets.')
    args = parser.parse_args()
    torch.set_num_threads(2)
    if (args.checkpoint or args.reference_checkpoint) and not args.trainer_config:
        parser.error('Neural checkpoints require their trainer config.')
    network = json.loads(args.trainer_config.read_text())['behaviors']['BlockNationsSeatV2']['network_settings'] if args.trainer_config else None
    candidate = FrozenActor(args.checkpoint, network) if args.checkpoint else TacticalActor(args.reference_work)
    reference = FrozenActor(args.reference_checkpoint, network) if args.reference_checkpoint else None
    result = evaluate(args.worker, args.destination, candidate, reference, games_per_cell=args.games_per_cell,
        seed=args.seed, reference_type=args.reference_recruit_type, reference_work=args.reference_work,
        difficulty=args.difficulty, reference_difficulty=args.reference_difficulty)
    print(json.dumps({key:value for key,value in result.items() if key != 'records'}, indent=2))
