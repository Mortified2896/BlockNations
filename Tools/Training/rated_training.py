"""Pinned ML-Agents adapter: rate whole matches, preserve stock PPO/self-play.

Only this process's trainer factory reference is replaced. Installed SDK files
are untouched. The adapter is intentionally pinned and fails on SDK drift.
"""
import os
from pathlib import Path
import signal
import time

import mlagents.trainers
from mlagents.trainers import learn as sdk_learn
from mlagents.trainers.ghost.trainer import GhostTrainer
from mlagents.trainers.behavior_id_utils import create_name_behavior_id
from match_rating import MatchRating, MatchJournalReader, policy_id
from interactive_environment import install as install_interactive_environment
from dotnet_environment import install as install_dotnet_environment
from training_contract import RULES_VERSION
from training_opponents import load_recipe, recipe_id

ACTIVE_TRAINER = None


class WholeMatchGhostTrainer(GhostTrainer):
    def __init__(self, *args, **kwargs):
        self.run = Path(os.environ['BLOCKNATIONS_RATING_RUN'])
        self.session = os.environ['BLOCKNATIONS_RATING_SESSION']
        self.rating_path = self.run / 'match-elo.json'
        self.rating = MatchRating(self.run.name, int(os.environ['BLOCKNATIONS_RATING_BOARD']))
        self.rating.restore(self.rating_path)
        self.rating.data['rulesVersion'] = RULES_VERSION
        for worker in range(16):
            pending = self.rating.pending_for(worker)
            if pending and pending['session'] != self.session:
                self.rating.data['abandoned'] += 1
                self.rating.set_pending(worker, None)
        self.journal = MatchJournalReader(self.run)
        self.last_rating_save = 0
        self.policy_ids = {}
        self.snapshot_ids = {}
        recipe = load_recipe(self.run)
        self.challenger_ids = [recipe_id(recipe)] if recipe else []
        super().__init__(*args, **kwargs)
        # Never inherit the inflated legacy trajectory Elo.
        self.policy_elos = [self.rating.data['elo']] * len(self.policy_elos)
        self.rating.save(self.rating_path)
        global ACTIVE_TRAINER
        ACTIVE_TRAINER = self

    def _process_trajectory(self, trajectory):
        pass  # PPO still receives trajectories through GhostTrainer.advance.

    def identify_policy(self, policy):
        # SDK 1.1.0 loads Torch state in place. Tensor mutation versions avoid
        # copying/hashing megabytes of unchanged weights on every decision.
        stamp = tuple((name, value.data_ptr(), value._version) for name, value in policy.actor.state_dict().items())
        previous = self.policy_ids.get(id(policy))
        if previous is None or previous[0] != stamp:
            previous = (stamp, policy_id(policy.get_weights()))
            self.policy_ids[id(policy)] = previous
        return previous[1]

    def identify_snapshot(self, weights):
        key = id(weights)
        previous = self.snapshot_ids.get(key)
        if previous is None or previous[0] is not weights:
            previous = (weights, policy_id(weights))
            self.snapshot_ids[key] = previous
        return previous[1]

    def identities(self, learning_seat=None):
        seat = self._learning_team if learning_seat is None else learning_seat
        identifiers = []
        for owner in (seat, 1 - seat):
            try:
                identifier = self.identify_policy(self.get_policy(create_name_behavior_id(self.brain_name, owner)))
            except KeyError:
                # The SDK announces a zero-decision loser only at its terminal RPC.
                # Its assigned initial weights are the current snapshot, exactly as
                # GhostTrainer.create_policy loads them on that later announcement.
                identifier = self.identify_snapshot(self.current_policy_snapshot[self.brain_name])
            identifiers.append(identifier)
        return tuple(identifiers)

    def drain_results(self):
        for event in self.journal.events():
            if event["session"] != self.session and event["session"] not in self.rating.data["sequences"]:
                continue  # Previously consumed archives from retired sessions.
            context = None
            ids = None
            if event['kind'] == 'begin' and event['session'] == self.session:
                if 'worker' in event:
                    context = event.get('context')
                else:
                    try:
                        learner, opponent = self.identities()
                        context = dict(learningSeat=self._learning_team, learner=learner, opponent=opponent)
                    except KeyError:
                        pass  # Initial handshake can precede registration of the other seat.
            elif 'worker' in event:
                ids = tuple(event['currentIds']) if event.get('currentIds') else None
            elif self.rating.data['pending'] and self.rating.data['pending']['context']:
                ids = self.identities(self.rating.data['pending']['context']['learningSeat'])
            self.rating.accept(event, context, ids)

    def advance(self):
        self.drain_results()
        self.policy_elos[-1] = self.rating.data['elo']
        super().advance()
        # Register new weight revisions/snapshots at the current training rating,
        # preserving distinct immutable weight hashes rather than reusing pool slots.
        live = [self.identify_policy(policy) for policy in self.policies.values()]
        snapshots = [self.identify_snapshot(snapshot[self.brain_name]) for snapshot in self.policy_snapshots]
        keep = {id(snapshot[self.brain_name]) for snapshot in self.policy_snapshots}
        self.snapshot_ids = {key: value for key, value in self.snapshot_ids.items() if key in keep}
        for identifier in live + snapshots + self.challenger_ids:
            self.rating.register(identifier)
        self.rating.retain(live + snapshots + self.challenger_ids)
        if time.monotonic() - self.last_rating_save >= 2:
            self.rating.save(self.rating_path)
            self.last_rating_save = time.monotonic()

    def save_model(self):
        self.drain_results()
        self.policy_elos[-1] = self.rating.data['elo']
        self.rating.save(self.rating_path)
        super().save_model()


def main():
    if mlagents.trainers.__version__ != '1.1.0':
        raise RuntimeError('Whole-match rating adapter requires audited ML-Agents 1.1.0.')
    import mlagents.trainers.trainer.trainer_factory as factory
    factory.GhostTrainer = WholeMatchGhostTrainer
    from human_imitation import install as install_human_imitation
    install_human_imitation(factory)
    def assignment():
        if ACTIVE_TRAINER is None:
            return None
        learner, opponent = ACTIVE_TRAINER.identities()
        return dict(learningSeat=ACTIVE_TRAINER._learning_team, learner=learner, opponent=opponent)
    if not install_dotnet_environment(sdk_learn, assignment):
        install_interactive_environment(sdk_learn)
    previous = signal.getsignal(signal.SIGINT)
    def interrupt(signum, frame):
        if ACTIVE_TRAINER is not None:
            ACTIVE_TRAINER.drain_results()
            ACTIVE_TRAINER.rating.save(ACTIVE_TRAINER.rating_path)
        if callable(previous):
            previous(signum, frame)
        else:
            raise KeyboardInterrupt
    signal.signal(signal.SIGINT, interrupt)
    try:
        sdk_learn.main()
    finally:
        if ACTIVE_TRAINER is not None:
            ACTIVE_TRAINER.drain_results()
            ACTIVE_TRAINER.rating.save(ACTIVE_TRAINER.rating_path)


if __name__ == '__main__':
    main()
