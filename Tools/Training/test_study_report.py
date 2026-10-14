import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from study_report import report, digest, DEFINITION
from supervisor import ROOT_MARKER


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value))


class StudyReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        write(self.root/ROOT_MARKER, dict(owner='BlockNations.LocalTraining.v1'))
        self.destination = self.root/'benchmarks/example'
        self.parent_path, self.parent = self.actor('parent', 100)
        self.study = dict(owner='BlockNations.InformationStudy.v1', name='example', treatment='capacity',
                          seeds=[84,85,86], state='running', stepsPerArm=2048, confirmation=True,
                          pins={str(self.parent_path):digest(self.parent_path)}, results=[])
        self.pack(self.destination/'baseline', self.parent, 2)
        self.pack(self.destination/'baseline-confirmation', self.parent, 2, confirmation=True)
        self.save()

    def tearDown(self):
        self.temp.cleanup()

    def save(self):
        write(self.destination/'study.json', self.study)

    def actor(self, name, step):
        directory = self.root/'runs'/name/'frozen-evaluations/model'; directory.mkdir(parents=True)
        checkpoint = directory/'actor.pt'; checkpoint.write_bytes(name.encode())
        config = directory/'trainer.yaml'; write(config, dict(network=name))
        value = dict(checkpoint=str(checkpoint), trainerConfig=str(config), step=step,
                     sha256=digest(checkpoint), trainerConfigSha256=digest(config))
        path = directory/'manifest.json'; write(path, value)
        return path, value

    def pack(self, directory, frozen, wins, *, confirmation=False):
        cases = [dict(name='reference-'+str(i), seed=1000+i+(10000 if confirmation else 0),
                      gamesPerCell=2, difficulty='Hard', referenceDifficulty='Hard',
                      reference=dict(kind='frozen',sha256='reference-'+str(i))) for i in range(3)]
        value = dict(version=1, rulesVersion='profile', boardSize=7, openingEconomyVersion=2,
                     workerSha256='worker', packages=dict(torch='pinned'), evaluatorSources=dict(evaluate='pinned'),
                     cases=cases)
        value['suiteId'] = hashlib.sha256(json.dumps(value,sort_keys=True).encode()).hexdigest()
        value.update(state='completed',candidate=dict(sha256=frozen['sha256'],configSha256=frozen['trainerConfigSha256']),results=[])
        for case in cases:
            records=[]
            for seat in (0,1):
                for first in (0,1):
                    for worker in (0,1):
                        # Four matches per role; distribute the requested wins.
                        index=seat*2+worker
                        records.append(dict(candidateSeat=seat,firstSeat=first,worker=worker,game=0,
                                            winner=seat if index<wins else 1-seat,interrupted=False))
            roles=[]
            for starts in (True,False):
                selected=[r for r in records if (r['candidateSeat']==r['firstSeat'])==starts]
                w=sum(r['winner']==r['candidateSeat'] for r in selected)
                roles.append(dict(candidateStarts=starts,games=4,wins=w,losses=4-w,interruptions=0))
            result=dict(version=1,rulesVersion='profile',boardSize=7,openingEconomyVersion=2,
                        seed=case['seed'],difficulty='Hard',referenceDifficulty='Hard',
                        candidateSha256=frozen['sha256'],referenceSha256=case['reference']['sha256'],
                        games=8,wins=2*wins,losses=8-2*wins,interruptions=0,startingPositionResults=roles)
            write(directory/case['name']/'evaluation.json',dict(result,records=records))
            value['results'].append(dict(case=case['name'],**result))
        write(directory/'suite.json',value)
        return value

    def add_arm(self, seed, arm, wins):
        path,frozen=self.actor(str(seed)+'-'+arm,2148)
        folder=self.destination/(str(seed)+'-'+arm)
        self.pack(folder/'fixed-results',frozen,wins)
        self.pack(folder/'confirmation-results',frozen,wins,confirmation=True)
        self.study['results'].append(dict(seed=seed,arm=arm,manifest=str(path),actualStep=2148,
                                         rejections=0,optimizerStepDeltas={'0':4},elapsedSeconds=12,
                                         externalWins=6*wins,externalGames=24,
                                         confirmationWins=6*wins,confirmationGames=24))
        self.save()

    def test_running_or_partial_study_never_becomes_a_completed_gain(self):
        value=report(self.root,self.destination)
        self.assertFalse(value['complete']);self.assertIsNone(value['replicatedPositivePairedMeans'])
        self.assertEqual(value['baseline']['primary']['wins'],12)
        self.assertEqual(value['comparisons'][0]['state'],'pending')
        self.add_arm(84,'control',2)
        value=report(self.root,self.destination)
        self.assertEqual(value['completedArms'],1);self.assertEqual(value['comparisons'][0]['state'],'partial')
        self.assertIsNone(value['replicatedPositivePairedMeans'])

    def test_all_three_seed_pairs_and_confirmation_remain_separate(self):
        for seed in self.study['seeds']:
            self.add_arm(seed,'control',2);self.add_arm(seed,'treatment',3)
        self.study['state']='completed';self.save()
        value=report(self.root,self.destination)
        self.assertTrue(value['complete']);self.assertTrue(value['replicatedPositivePairedMeans'])
        for row in value['comparisons']:
            self.assertAlmostEqual(row['treatmentMinusControl']['primary']['meanGain'],.25)
            self.assertAlmostEqual(row['treatmentMinusControl']['confirmation']['meanGain'],.25)
        self.assertIn('not IID',value['interpretation'])

    def test_one_lucky_seed_cannot_hide_two_paired_regressions(self):
        for seed,wins in [(84,4),(85,1),(86,1)]:
            self.add_arm(seed,'control',2);self.add_arm(seed,'treatment',wins)
        self.study['state']='completed';self.save()
        value=report(self.root,self.destination)
        self.assertFalse(value['replicatedPositivePairedMeans'])
        self.assertEqual(len(value['comparisons']),3)

    def test_changed_model_and_missing_or_duplicate_raw_games_fail_closed(self):
        path=self.destination/'baseline/suite.json'
        raw=path.parent/'reference-0/evaluation.json';original=json.loads(raw.read_text())
        for change in [lambda d:d['records'].pop(),lambda d:d['records'].__setitem__(0,d['records'][1]),
                       lambda d:d['records'][0].update(winner=1),lambda d:d['startingPositionResults'][0].update(wins=3)]:
            value=copy.deepcopy(original);change(value);write(raw,value)
            with self.assertRaises(ValueError):report(self.root,self.destination)
        write(raw,original)
        Path(self.parent['checkpoint']).write_bytes(b'changed')
        with self.assertRaises(ValueError):report(self.root,self.destination)

    def test_changed_pack_identity_rules_or_confirmation_actor_are_rejected(self):
        path=self.destination/'baseline-confirmation/suite.json';original=json.loads(path.read_text())
        for change in [lambda d:d.update(workerSha256='different'),lambda d:d.update(rulesVersion='new'),
                       lambda d:d['cases'][0].update(seed=1000),lambda d:d['candidate'].update(sha256='other')]:
            value=copy.deepcopy(original);change(value)
            canonical={k:value[k] for k in DEFINITION if k in value}
            value['suiteId']=hashlib.sha256(json.dumps(canonical,sort_keys=True).encode()).hexdigest();write(path,value)
            with self.assertRaises(ValueError):report(self.root,self.destination)
        write(path,original)

    def test_study_aggregate_cannot_disagree_with_raw_arm_outcomes(self):
        self.add_arm(84,'control',2)
        self.study['results'][0]['externalWins']=20;self.save()
        with self.assertRaises(ValueError):report(self.root,self.destination)

    def test_missing_pair_or_duplicated_arm_cannot_complete(self):
        self.add_arm(84,'control',2)
        self.study['state']='completed';self.save()
        with self.assertRaises(ValueError):report(self.root,self.destination)
        self.study['state']='running';self.study['results'].append(self.study['results'][0]);self.save()
        with self.assertRaises(ValueError):report(self.root,self.destination)


if __name__=='__main__':unittest.main()
