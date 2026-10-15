import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
import torch

from test_initialization import arrays, prepared
from rollout import FrozenStudent, behaviour_distribution
from training_contract import CORE_THREE_RULES
from corpus import OWNER, load_many, validate


def corpus(root, name, *, offset=0, leak=False, teacher='a', partition='training'):
    folder=root/'benchmarks'/name;folder.mkdir(parents=True)
    data=arrays()
    if leak:
        data['validation']=(data['group']==1)|(data['group']==2)
    data['group']+=offset
    path=folder/'corpus.npz';np.savez_compressed(path,**data)
    record=dict(owner=OWNER,state='completed',partition=partition,observationVersion=4,actionVersion=2,
        rulesVersion=CORE_THREE_RULES,boardSize=7,openingEconomyVersion=2,diagnosticFullVisionSeats=0,
        humanIngestion='none',teacher=dict(sha256=teacher*64),teacherSampling=dict(mode='Policy',settings=None),
        corpus=str(path),corpusSha256=hashlib.sha256(path.read_bytes()).hexdigest(),**validate(data))
    manifest=folder/'manifest.json';manifest.write_text(json.dumps(record))
    return manifest


class AggregateCorpusTests(unittest.TestCase):
    def test_aggregation_keeps_whole_match_splits_and_binds_each_source(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();one=corpus(root,'one');two=corpus(root,'two',offset=100)
            data,record=load_many(root,[one,two])
            self.assertEqual(record['decisions'],256)
            self.assertEqual(record['trainingGroups'],12);self.assertEqual(record['validationGroups'],4)
            self.assertEqual(len(record['corpusSources']),2)
            self.assertFalse(np.intersect1d(data['group'][data['validation']],data['group'][~data['validation']]).size)
            self.assertEqual(record['corpusIdentityKind'],'ordered-source-manifest-and-corpus-sha256-list')
            self.assertNotEqual(load_many(root,one)[1]['corpusSha256'],record['corpusSha256'])

    def test_duplicate_teacher_changes_and_cross_corpus_leaks_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();one=corpus(root,'one');other=corpus(root,'other',offset=100,teacher='b')
            leaking=corpus(root,'leaking',leak=True);evaluation=corpus(root,'evaluation',offset=200,partition='evaluation-only')
            for pair in ([one,one],[one,other],[one,leaking],[one,evaluation]):
                with self.assertRaises(ValueError):load_many(root,pair)
            with self.assertRaises(ValueError):load_many(root,[])
            with self.assertRaises(ValueError):load_many(root,[one]*5)


class FrozenRolloutTests(unittest.TestCase):
    def test_mixture_changes_behaviour_without_mutating_expert_labels(self):
        teacher=np.zeros((1,259),np.float32);teacher[0,0]=1
        student=np.zeros_like(teacher);student[0,-1]=1
        mixed=behaviour_distribution(teacher,student,.25)
        self.assertEqual(mixed[0,0],.25);self.assertEqual(mixed[0,-1],.75)
        self.assertEqual(teacher[0,0],1);self.assertEqual(teacher[0,-1],0)
        np.testing.assert_array_equal(behaviour_distribution(teacher,student,1),teacher)
        np.testing.assert_array_equal(behaviour_distribution(teacher,student,0),student)
        with self.assertRaises(ValueError):behaviour_distribution(teacher,student,float('nan'))

    def test_student_requires_immutable_prepared_weights_and_current_fair_contract(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();manifest,path=prepared(root/'benchmarks/student')
            student=FrozenStudent(root,manifest,rules=CORE_THREE_RULES,board_size=7)
            data=arrays();actual=student.probabilities(data['observations'],data['allowed'])
            self.assertTrue(np.isfinite(actual).all());self.assertTrue((actual[~data['allowed']]==0).all())
            with self.assertRaises(ValueError):FrozenStudent(root,manifest,rules=CORE_THREE_RULES,board_size=9)
            saved=torch.load(path,map_location='cpu');saved['global_step']['_GlobalSteps__global_step'].fill_(10)
            torch.save(saved,path)
            with self.assertRaises(ValueError):FrozenStudent(root,manifest,rules=CORE_THREE_RULES,board_size=7)


if __name__=='__main__':unittest.main()
