import hashlib
import json
import os
from pathlib import Path
import tempfile
import shutil
import unittest
from unittest.mock import Mock, patch

from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from dotnet_environment import SPEC
from playtest import PlaytestSession, BEHAVIOR
from playtest_champion import select, preferred_checkpoint, POINTER
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION


class PlaytestChampionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name)/'owned-run'
        self.run.mkdir()
        self.contract = dict(rulesVersion=RULES_VERSION, openingEconomyVersion=OPENING_ECONOMY_VERSION, boardSize=7)
        self.write(self.run/'run.json', dict(self.contract, owner='BlockNations.LocalTraining.v1', schema=2,
                   observationSize=3120, actionCount=259, seed=42))
        self.network = dict(hidden_units=8, num_layers=1, normalize=False)
        self.config = {'behaviors': {BEHAVIOR: {'network_settings': self.network}}}
        self.write(self.run/'trainer.yaml', self.config)
        self.actor = SimpleActor(SPEC.observation_specs, NetworkSettings(**self.network), SPEC.action_spec)
        self.models = self.run/'checkpoints'/self.run.name/BEHAVIOR
        self.models.mkdir(parents=True)
        self.latest = self.models/(BEHAVIOR+'-900.pt')
        torch.save({'Policy': self.actor.state_dict()}, self.latest)
        self.latest.with_suffix('.onnx').write_bytes(b'complete export')
        os.utime(self.latest.with_suffix('.onnx'), (1, 1))

    def tearDown(self):
        self.temp.cleanup()

    def write(self, path, data):
        path.write_text(json.dumps(data))

    def candidate(self, step, wins):
        from match_rating import policy_id
        directory = self.run/'frozen-evaluations'/str(step)
        directory.mkdir(parents=True)
        checkpoint = directory/f'{BEHAVIOR}-{step}.pt'
        torch.save({'Policy': self.actor.state_dict()}, checkpoint)
        config = directory/'trainer.yaml'
        self.write(config, self.config)
        digest = hashlib.sha256(checkpoint.read_bytes()).hexdigest()
        identity = dict(sha256=digest, configSha256=hashlib.sha256(config.read_bytes()).hexdigest(),
                        actorId=policy_id(self.actor.state_dict()))
        manifest = directory/'manifest.json'
        self.write(manifest, dict(self.contract, step=step, checkpoint=str(checkpoint), trainerConfig=str(config),
                                 sha256=digest, actorId=identity['actorId']))
        evaluation = self.run/'evaluations'/str(step)
        (evaluation/'fixed').mkdir(parents=True)
        records = [dict(firstSeat=first, candidateSeat=seat, interrupted=False,
                        winner=seat if index < wins else 1-seat)
                   for first in (0, 1) for seat in (0, 1) for index in range(4)]
        result = dict(self.contract, candidateSha256=digest, games=16, wins=wins*4,
                      seed=42, difficulty='Hard', referenceDifficulty='Hard', reference='fixture-tactician',
                      referenceWork=64, referenceRecruitType='',
                      losses=16-wins*4, interruptions=0,
                      startingPositionResults=[dict(candidateStarts=starts, games=8, wins=wins*2,
                                                    losses=8-wins*2, interruptions=0) for starts in (True, False)])
        self.write(evaluation/'fixed/evaluation.json', dict(result, records=records))
        self.write(evaluation/'suite.json', dict(self.contract, state='completed', suiteId='a'*64, candidate=identity,
                  cases=[dict(name='fixed', gamesPerCell=4, seed=42, difficulty='Hard', referenceDifficulty='Hard',
                              reference=dict(kind='tactical', implementation='fixture-tactician', workBudget=64, recruitType=''))],
                  results=[dict(result, case='fixed')]))
        return checkpoint, manifest, evaluation/'suite.json'

    def test_champion_selection_uses_older_evaluated_actor_and_latest_is_explicit(self):
        checkpoint, manifest, evaluation = self.candidate(100, 2)
        self.assertEqual(preferred_checkpoint(self.run), (self.latest, 'latest completed checkpoint'))
        select(self.run, manifest, evaluation, initialize=True)
        self.assertEqual(preferred_checkpoint(self.run), (checkpoint, 'evaluated champion'))
        self.assertEqual(preferred_checkpoint(self.run, 'Latest')[0], self.latest)
        with patch('playtest.subprocess.Popen') as launch:
            process = Mock(); process.poll.return_value = None; launch.return_value = process
            session = PlaytestSession(self.run, Path('/Training.app'))
            session.start(dict(requestId='a'*32, difficulty='Hard'), {})
            self.assertEqual(session.state['checkpoint'], checkpoint.stem)
            self.assertEqual(session.state['checkpointSelection'], 'evaluated champion')
            session.close()

    def test_regression_or_changed_suite_preserves_incumbent_then_improvement_can_promote(self):
        _, manifest, evaluation = self.candidate(100, 2)
        select(self.run, manifest, evaluation, initialize=True)
        original = (self.run/POINTER).read_bytes()
        _, worse, worse_results = self.candidate(200, 1)
        with self.assertRaisesRegex(ValueError, 'regressed'):
            select(self.run, worse, worse_results)
        self.assertEqual((self.run/POINTER).read_bytes(), original)
        new, better, better_results = self.candidate(300, 3)
        data = json.loads(better_results.read_text()); data['suiteId'] = 'b'*64
        self.write(better_results, data)
        with self.assertRaisesRegex(ValueError, 'identical benchmark'):
            select(self.run, better, better_results)
        self.assertEqual((self.run/POINTER).read_bytes(), original)
        data['suiteId'] = 'a'*64; self.write(better_results, data)
        select(self.run, better, better_results)
        self.assertEqual(preferred_checkpoint(self.run)[0], new)

    def test_tampered_model_missing_records_and_foreign_rules_cannot_be_selected(self):
        checkpoint, manifest, evaluation = self.candidate(100, 2)
        records_path = evaluation.parent/'fixed/evaluation.json'
        full = records_path.read_bytes()
        data = json.loads(full); data['records'].pop(); self.write(records_path, data)
        with self.assertRaisesRegex(ValueError, 'match records'):
            select(self.run, manifest, evaluation, initialize=True)
        records_path.write_bytes(full)
        data = json.loads(manifest.read_text()); data['rulesVersion'] = 'other-rules'; self.write(manifest, data)
        with self.assertRaisesRegex(ValueError, 'rules, board'):
            select(self.run, manifest, evaluation, initialize=True)
        data['rulesVersion'] = RULES_VERSION; self.write(manifest, data)
        select(self.run, manifest, evaluation, initialize=True)
        checkpoint.write_bytes(b'tampered weights')
        with self.assertRaises(ValueError):
            preferred_checkpoint(self.run)  # Fail explicitly, never silently switch to newest.

    def test_linked_pointer_is_rejected_and_completed_checkpoint_retirement_is_safe(self):
        checkpoint, manifest, evaluation = self.candidate(100, 2)
        select(self.run, manifest, evaluation, initialize=True)
        self.latest.unlink(); self.latest.with_suffix('.onnx').unlink()
        self.assertEqual(preferred_checkpoint(self.run)[0], checkpoint)
        pointer = self.run/POINTER
        saved = pointer.with_suffix('.saved'); pointer.rename(saved); pointer.symlink_to(saved)
        with self.assertRaisesRegex(ValueError, 'linked'):
            preferred_checkpoint(self.run)

    def test_source_refresh_keeps_exact_actor_and_original_receipt(self):
        from tournament_selection import refresh
        checkpoint, manifest, evaluation = self.candidate(100, 2)
        select(self.run, manifest, evaluation, initialize=True)
        original = (self.run/POINTER).read_bytes()
        target = evaluation.parent.with_name('new-baseline')
        shutil.copytree(evaluation.parent, target)
        receipt = json.loads((target/'suite.json').read_text());receipt['suiteId'] = 'b'*64
        self.write(target/'suite.json', receipt)
        refresh(self.run, target/'suite.json')
        self.assertEqual(preferred_checkpoint(self.run)[0], checkpoint)
        self.assertEqual(next((self.run/'champion-history').glob('*.json')).read_text().strip(), json.dumps(json.loads(original), indent=2))
        self.assertTrue(evaluation.exists())

    def test_direct_winner_promotes_despite_reference_regression_and_protects_evidence(self):
        old, old_manifest, old_results = self.candidate(100, 4)
        new, new_manifest, new_results = self.candidate(200, 1)
        select(self.run, old_manifest, old_results, initialize=True)
        old_identity = json.loads(old_results.read_text())['candidate']
        new_identity = json.loads(new_results.read_text())['candidate']
        paths = {}
        for label, seed in (('primary', 610014), ('confirmation', 1610017)):
            target = self.run/'evaluations'/('direct-'+label); target.mkdir()
            cases, results = [], []
            for mode in ('Hard', 'Policy'):
                name = 'champion-'+mode.lower(); (target/name).mkdir()
                records = [dict(firstSeat=first, candidateSeat=seat, interrupted=False, winner=seat)
                           for first in (0, 1) for seat in (0, 1) for _ in range(32)]
                result = dict(self.contract, candidateSha256=new_identity['sha256'], referenceSha256=old_identity['sha256'],
                              seed=seed, difficulty=mode, referenceDifficulty=mode,
                              games=128, wins=128, losses=0, interruptions=0,
                              startingPositionResults=[dict(candidateStarts=s, games=64, wins=64, losses=0, interruptions=0)
                                                       for s in (True, False)])
                self.write(target/name/'evaluation.json', dict(result, records=records))
                cases.append(dict(name=name, seed=seed, gamesPerCell=32, difficulty=mode, referenceDifficulty=mode,
                                  reference=dict(kind='frozen', **old_identity)))
                results.append(dict(result, case=name))
            receipt = dict(self.contract, candidate=new_identity, cases=cases, results=results, state='completed', suiteId=label)
            paths[label] = target/'suite.json'; self.write(paths[label], receipt)
        select(self.run, new_manifest, new_results, head_to_head=paths)
        self.assertEqual(preferred_checkpoint(self.run)[0], new)
        pointer = json.loads((self.run/POINTER).read_text())
        self.assertEqual(pointer['promotionEvidence']['criterion'], 'direct-incumbent-v1')
        paths['confirmation'].write_text('{}')
        with self.assertRaisesRegex(ValueError, 'direct-match evidence changed'):
            preferred_checkpoint(self.run)

    def test_replicated_promotion_checks_actual_records_for_both_actors(self):
        old, old_manifest, old_results = self.candidate(100, 2)
        new, new_manifest, new_results = self.candidate(200, 3)
        select(self.run, old_manifest, old_results, initialize=True)
        confirmations = []
        for primary in (old_results, new_results):
            target = primary.parent.with_name(primary.parent.name+'-repeat')
            shutil.copytree(primary.parent, target)
            receipt = json.loads((target/'suite.json').read_text())
            receipt['suiteId'] = 'c'*64;receipt['cases'][0]['seed'] += 1000003;receipt['results'][0]['seed'] += 1000003
            full = json.loads((target/'fixed/evaluation.json').read_text());full['seed'] += 1000003
            self.write(target/'fixed/evaluation.json', full);self.write(target/'suite.json', receipt)
            confirmations.append(target/'suite.json')
        select(self.run, new_manifest, new_results, confirmation=dict(incumbent=confirmations[0], candidate=confirmations[1]))
        self.assertEqual(preferred_checkpoint(self.run)[0], new)
        self.assertTrue(json.loads((self.run/POINTER).read_text())['promotionEvidence']['accepted'])


if __name__ == '__main__':
    unittest.main()
