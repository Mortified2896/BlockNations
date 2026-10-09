"""Bounded, fair human demonstrations alongside stock on-policy self-play.

Inference games are never fed into PPO as on-policy experience. A few masked
imitation minibatches run after a PPO update, before publishing its new weights.
The existing checkpointed Adam optimizer is retained; no API or external model.
"""
import base64
import hashlib
import json
from pathlib import Path
import time
import uuid

import numpy as np

from training_contract import RULES_VERSION, OBSERVATIONS, ACTIONS
MAXIMUM_SAMPLES = 1024
MAXIMUM_FILE_BYTES = 20 * 1024 * 1024
MAXIMUM_GAMES = 32
PASSES_PER_GAME, BATCH_SIZE, BATCHES_PER_UPDATE = 16, 64, 4


def decode_game(path, board):
    if path.is_symlink() or path.stat().st_size > MAXIMUM_FILE_BYTES:
        raise ValueError('Human recording is linked or oversized.')
    contents = path.read_bytes()
    game = json.loads(contents)
    if (game.get('version') != 1 or game.get('schema') != 2 or
            game.get('rulesVersion') != RULES_VERSION or game.get('boardSize') != board):
        return None  # Preserve recordings from other rule/board configurations.
    if (game.get('humanSeat') not in (0, 1) or game.get('winnerSeat') not in (-1, 0, 1) or
            uuid.UUID(hex=game['id']).hex != game['id'] or path.stem != game['id']):
        raise ValueError('Invalid human recording identity or outcome.')
    if game.get('completed') is not True or game['winnerSeat'] != game['humanSeat']:
        return None  # Losses/unfinished games are review data, not winning examples.
    samples = game.get('samples')
    if not isinstance(samples, list) or not 1 <= len(samples) <= MAXIMUM_SAMPLES:
        raise ValueError('Invalid number of human examples.')
    observations, masks, actions = [], [], []
    for sample in samples:
        action, legal = sample.get('action'), sample.get('legal')
        if (type(action) is not int or not 0 <= action < ACTIONS or not isinstance(legal, list) or
                not 1 <= len(legal) <= ACTIONS or any(type(i) is not int or not 0 <= i < ACTIONS for i in legal) or
                len(set(legal)) != len(legal) or action not in legal or ACTIONS - 1 not in legal):
            raise ValueError('Human example is outside its legal action mask.')
        encoded = sample.get('observation', '')
        if not isinstance(encoded, str) or len(encoded) != OBSERVATIONS * 4 // 3 * 4:
            raise ValueError('Invalid human observation size.')
        values = np.frombuffer(base64.b64decode(encoded, validate=True), dtype='<f4')
        if values.size != OBSERVATIONS or not np.isfinite(values).all() or values[-1] != 2:
            raise ValueError('Invalid human observation tensor.')
        mask = np.zeros(ACTIONS, dtype=np.float32)
        mask[legal] = 1  # Torch actor masks use one for allowed actions.
        observations.append(values); masks.append(mask); actions.append(action)
    return (hashlib.sha256(contents).hexdigest(), np.stack(observations), np.stack(masks), np.array(actions, dtype=np.int64))


class HumanImitation:
    def __init__(self, run, board, seed):
        self.run, self.board = Path(run), board
        self.random = np.random.default_rng(seed)
        self.cache, self.stamps = {}, {}
        self.consumed = {}
        self.updates = self.used_examples = 0
        self.last_step, self.last_loss = 0, None
        self.next_poll = 0
        self.error = ''
        self.saved_games = 0
        path = self.run / 'human-learning-status.json'
        if path.exists():
            state = json.loads(path.read_text())
            if (state.get('version') != 1 or state.get('runId') != self.run.name or
                    state.get('boardSize') != board or state.get('rulesVersion') != RULES_VERSION):
                raise ValueError('Human learning ledger belongs to different rules/run/board.')
            self.consumed = state.get('consumed', {})
            if len(self.consumed) > 512 or any(type(v) is not int or v < 0 for v in self.consumed.values()):
                raise ValueError('Invalid human learning consumption ledger.')
            self.updates, self.used_examples = state['updates'], state['usedExamples']
            self.last_step, self.last_loss = state['lastStep'], state['lastLoss']
        self.poll(force=True)

    def poll(self, force=False):
        if not force and time.monotonic() < self.next_poll:
            return
        self.next_poll = time.monotonic() + 5
        directory = self.run / 'human-games'
        if directory.is_symlink():
            self.error = 'Human recording directory is linked; ingestion disabled.'
            self.cache.clear(); self.save(); return
        files = sorted(directory.glob('*.json'), key=lambda p: p.stat().st_mtime, reverse=True)[:MAXIMUM_GAMES]
        self.saved_games = len(files)
        keep = set(files)
        self.cache = {p: value for p, value in self.cache.items() if p in keep}
        self.stamps = {p: value for p, value in self.stamps.items() if p in keep}
        self.error = ''
        for path in files:
            stamp = (path.stat().st_mtime_ns, path.stat().st_size)
            if self.stamps.get(path) == stamp:
                continue
            self.stamps[path] = stamp
            self.cache.pop(path, None)
            try:
                value = decode_game(path, self.board)
                if value is not None:
                    self.cache[path] = value
            except (OSError, ValueError, KeyError, TypeError) as error:
                self.error = 'Human recording skipped: ' + str(error)
        self.save()

    def sample_batch(self):
        eligible = [value for value in self.cache.values()
                    if self.consumed.get(value[0], 0) < max(128, len(value[3]) * PASSES_PER_GAME)]
        if not eligible:
            return None
        value = eligible[int(self.random.integers(len(eligible)))]  # Equal games, not longer games dominating.
        key, observations, masks, actions = value
        remaining = max(128, len(actions) * PASSES_PER_GAME) - self.consumed.get(key, 0)
        indices = self.random.integers(len(actions), size=min(BATCH_SIZE, remaining))
        return key, observations[indices], masks[indices], actions[indices]

    def update(self, policy, optimizer, step):
        from mlagents.torch_utils import torch
        from mlagents.trainers.torch_entities.agent_action import AgentAction
        self.poll()
        device = next(policy.actor.parameters()).device
        losses = []
        for _ in range(BATCHES_PER_UPDATE):
            batch = self.sample_batch()
            if batch is None:
                break
            key, observations, masks, choices = batch
            stats = policy.actor.get_stats(
                [torch.as_tensor(observations, device=device)],
                AgentAction(None, [torch.as_tensor(choices[:, None], device=device)]),
                masks=torch.as_tensor(masks, device=device))
            loss = -stats['log_probs'].flatten().mean()
            if not torch.isfinite(loss):
                raise ValueError('Human imitation produced a non-finite loss.')
            previous_rates = [group['lr'] for group in optimizer.param_groups]
            try:
                for group in optimizer.param_groups:
                    group['lr'] *= .5
                optimizer.zero_grad(set_to_none=True)
                loss.backward()
                torch.nn.utils.clip_grad_norm_(policy.actor.parameters(), .5)
                optimizer.step()
            finally:
                for group, rate in zip(optimizer.param_groups, previous_rates):
                    group['lr'] = rate
            self.consumed[key] = self.consumed.get(key, 0) + len(choices)
            self.used_examples += len(choices)
            self.updates += 1
            self.last_step, self.last_loss = int(step), float(loss.detach().cpu())
            losses.append(self.last_loss)
        self.save()
        return float(np.mean(losses)) if losses else None

    def save(self):
        # Retain a bounded replay-protection ledger even after old games are pruned.
        if len(self.consumed) > 512:
            self.consumed = dict(list(self.consumed.items())[-512:])
        state = dict(version=1, runId=self.run.name, boardSize=self.board, rulesVersion=RULES_VERSION,
                     savedGames=self.saved_games, winningGames=len(self.cache), samples=sum(len(v[3]) for v in self.cache.values()),
                     usedExamples=self.used_examples, updates=self.updates, lastStep=self.last_step,
                     lastLoss=self.last_loss, consumed=self.consumed, error=self.error)
        temporary = self.run / 'human-learning-status.json.tmp'
        temporary.write_text(json.dumps(state) + '\n')
        temporary.replace(self.run / 'human-learning-status.json')


def install(factory):
    from mlagents.trainers.ppo.trainer import PPOTrainer
    import os
    class HumanImitationPPOTrainer(PPOTrainer):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.human = HumanImitation(os.environ['BLOCKNATIONS_RATING_RUN'],
                                       int(os.environ['BLOCKNATIONS_RATING_BOARD']), self.seed)

        def _update_policy(self):
            updated = super()._update_policy()
            if updated:
                loss = self.human.update(self.policy, self.optimizer.optimizer, self.get_step)
                if loss is not None:
                    self._stats_reporter.add_stat('Losses/Human Imitation', loss)
            return updated
    # SDK publishes/checkpoints this trainer's actor and existing optimizer normally.
    # learn.main registers plugins after imports, replacing the PPO entry. Install
    # at actual trainer construction, after that registration has finished.
    initialize = factory.TrainerFactory._initialize_trainer
    def initialize_with_human(*args, **kwargs):
        settings = args[0] if args else kwargs['trainer_settings']
        if settings.trainer_type == 'ppo':
            factory.all_trainer_types['ppo'] = HumanImitationPPOTrainer
        return initialize(*args, **kwargs)
    factory.TrainerFactory._initialize_trainer = staticmethod(initialize_with_human)
