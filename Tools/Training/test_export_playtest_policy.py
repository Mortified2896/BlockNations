import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import numpy as np
import onnx
from onnx import helper, numpy_helper, TensorProto

from export_playtest_policy import export, evaluated_transfer
from training_contract import RULES_VERSION, CORE_THREE_RULES, transfer_contract


class ExportPolicyTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.run = Path(self.scratch.name)/'run'; self.run.mkdir()
        self.checkpoint = self.run/'BlockNationsSeatV2-200.pt'
        self.checkpoint.write_bytes(b'toy immutable checkpoint')
        for name, data in [('run.json',dict(boardSize=7)),('arena-status.json',dict(simulationVersion=RULES_VERSION)),
                           ('checkpoint-contract.json',dict(rulesVersion=RULES_VERSION,boardSize=7,openingEconomyVersion=2,minimumCheckpointStep=100))]:
            (self.run/name).write_text(json.dumps(data))
        inputs = [helper.make_tensor_value_info('obs_0',TensorProto.FLOAT,[1,3120]),
                  helper.make_tensor_value_info('action_masks',TensorProto.FLOAT,[1,259])]
        values = [numpy_helper.from_array(np.array([value],dtype=np.int64),name) for name,value in [('start',0),('end',259),('axis',1)]]
        values += [numpy_helper.from_array(np.array(1,dtype=np.float32),'one'),numpy_helper.from_array(np.array(1e9,dtype=np.float32),'floor')]
        nodes = [helper.make_node('Slice',['obs_0','start','end','axis'],['logits']),
                 helper.make_node('Sub',['action_masks','one'],['blocked']),
                 helper.make_node('Mul',['blocked','floor'],['offset']),
                 helper.make_node('Add',['logits','offset'],['masked']),
                 helper.make_node('Softmax',['masked'],['probabilities'],axis=1)]
        graph=helper.make_graph(nodes,'toy masked actor',inputs,[helper.make_tensor_value_info('probabilities',TensorProto.FLOAT,[1,259])],values)
        onnx.save(helper.make_model(graph,opset_imports=[helper.make_opsetid('',15)]),self.checkpoint.with_suffix('.onnx'))

    def tearDown(self):
        self.scratch.cleanup()

    def test_actor_probability_parity_and_masking_are_verified_on_export(self):
        with patch('export_playtest_policy.latest_checkpoint',return_value=self.checkpoint):
            result=export(self.run,self.run/'export')
        self.assertEqual(result['verification']['samples'],24)
        self.assertEqual(result['verification']['maximumProbabilityError'],0)
        self.assertTrue(result['verification']['maskedActionsVerified'])
        self.assertEqual(result['checkpointSha256'],hashlib.sha256(self.checkpoint.read_bytes()).hexdigest())
        self.assertEqual(result['rulesVersion'],RULES_VERSION)

    def test_live_new_rules_cannot_relabel_a_pretransition_or_unrecorded_checkpoint(self):
        old=self.checkpoint.with_name('BlockNationsSeatV2-100.pt')
        with patch('export_playtest_policy.latest_checkpoint',return_value=old):
            with self.assertRaisesRegex(ValueError,'after the recorded'):
                export(self.run,self.run/'old-export')
        (self.run/'checkpoint-contract.json').unlink()
        with self.assertRaisesRegex(ValueError,'recorded training contract'):
            export(self.run,self.run/'unrecorded-export')
        self.assertFalse((self.run/'old-export').exists())

    def test_explicit_frozen_selection_requires_matching_rules_and_content_hash(self):
        metadata=dict(rulesVersion=RULES_VERSION,boardSize=7,openingEconomyVersion=2,
                      sha256=hashlib.sha256(self.checkpoint.read_bytes()).hexdigest())
        (self.run/'manifest.json').write_text(json.dumps(metadata))
        export(self.run,self.run/'frozen-export',self.checkpoint)
        self.checkpoint.write_bytes(b'tampered checkpoint')
        with self.assertRaisesRegex(ValueError,'provenance'):
            export(self.run,self.run/'tampered-export',self.checkpoint)
        self.assertFalse((self.run/'tampered-export').exists())

    def test_core_three_exports_require_matching_run_arena_and_checkpoint_contract(self):
        (self.run/'run.json').write_text(json.dumps(dict(boardSize=7,rulesVersion=CORE_THREE_RULES)))
        with self.assertRaisesRegex(ValueError,'current rules'):
            export(self.run,self.run/'wrong-arena')
        (self.run/'arena-status.json').write_text(json.dumps(dict(simulationVersion=CORE_THREE_RULES)))
        with self.assertRaisesRegex(ValueError,'recorded training contract'):
            export(self.run,self.run/'wrong-contract')
        (self.run/'checkpoint-contract.json').write_text(json.dumps(dict(rulesVersion=CORE_THREE_RULES,
            boardSize=7,openingEconomyVersion=2,minimumCheckpointStep=100)))
        with patch('export_playtest_policy.latest_checkpoint',return_value=self.checkpoint):
            result=export(self.run,self.run/'core-three')
        self.assertEqual(result['rulesVersion'],CORE_THREE_RULES)
        self.assertEqual(result['checkpointSha256'],hashlib.sha256(self.checkpoint.read_bytes()).hexdigest())

    def test_transfer_needs_completed_target_rules_evaluation_of_the_exact_frozen_actor(self):
        manifest=dict(rulesVersion=RULES_VERSION,sha256='a'*64)
        receipt=dict(state='completed',boardSize=7,openingEconomyVersion=2,rulesVersion=CORE_THREE_RULES,
            suiteId='b'*64,results=[dict(games=64)],candidate=dict(sha256='a'*64,trainedRulesVersion=RULES_VERSION,
                transferRules=transfer_contract(RULES_VERSION,CORE_THREE_RULES)))
        self.assertEqual(evaluated_transfer(manifest,receipt,CORE_THREE_RULES),transfer_contract(RULES_VERSION,CORE_THREE_RULES))
        receipt['candidate']['configSha256']='d'*64
        self.assertEqual(evaluated_transfer(manifest,receipt,CORE_THREE_RULES,'d'*64),transfer_contract(RULES_VERSION,CORE_THREE_RULES))
        with self.assertRaisesRegex(ValueError,'completed matching'):
            evaluated_transfer(manifest,receipt,CORE_THREE_RULES,'e'*64)
        for key,value in [('state','running'),('rulesVersion',RULES_VERSION),('results',[]),('suiteId','')]:
            with self.subTest(key=key),self.assertRaisesRegex(ValueError,'completed matching'):
                evaluated_transfer(manifest,dict(receipt,**{key:value}),CORE_THREE_RULES)
        receipt['candidate']['sha256']='c'*64
        with self.assertRaisesRegex(ValueError,'completed matching'):
            evaluated_transfer(manifest,receipt,CORE_THREE_RULES)


if __name__ == '__main__':
    unittest.main()
