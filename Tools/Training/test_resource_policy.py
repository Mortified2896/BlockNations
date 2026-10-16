import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

from power_controller import tick
from resource_policy import DEFAULTS, WorkBudget, assess, parse_swap, policy


def sample(seconds=0, busy=20, memory=1, thermal=0, swap=0, load=1):
    return dict(cpuTicks=[busy*seconds, 0, (100-busy)*seconds, 0], memoryPressure=memory,
                thermalState=thermal, swapBytes=swap, load1=load, cores=8)


class ResourcePolicyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root/'resource-policy.json').write_text(json.dumps(DEFAULTS))

    def tearDown(self):
        self.temp.cleanup()

    def test_sustained_idle_required_and_busy_work_resets_restart_interval(self):
        state = {}
        for now in range(0, 121, 15):
            result=assess(self.root,state,False,now,sample(now))
            self.assertEqual(result['allowed'],now==120)
        result=assess(self.root,state,True,135,sample(135,load=8))
        self.assertFalse(result['allowed']);self.assertIn('cpu_busy',result['reason'])
        for now in range(150,271,15):
            result=assess(self.root,state,False,now,sample(now))
            self.assertEqual(result['allowed'],now==270)

    def test_cpu_hysteresis_and_current_load_block_first_sample(self):
        state={}
        self.assertIn('cpu_busy',assess(self.root,state,False,0,sample(load=6))['reason'])
        state={}
        assess(self.root,state,True,0,sample())
        self.assertTrue(assess(self.root,state,True,15,sample(15,busy=55))['allowed'])
        self.assertFalse(assess(self.root,{},False,15,sample(15,busy=55))['allowed'])
        self.assertFalse(assess(self.root,state,True,30,sample(30,busy=75))['allowed'])

    def test_memory_warmth_and_active_swap_yield_immediately(self):
        for reading,reason in ((sample(15,memory=2),'memory_pressure'),(sample(15,memory=4),'memory_pressure'),
                               (sample(15,thermal=1),'thermal_pressure'),
                               (sample(15,swap=30_000_000),'active_swapping')):
            state={};assess(self.root,state,True,0,sample())
            result=assess(self.root,state,True,15,reading)
            self.assertFalse(result['allowed']);self.assertIn(reason,result['reason'])

    def test_unknown_and_stale_samples_cannot_resume_or_latch_a_fault(self):
        state={}
        for now in range(0,121,15):assess(self.root,state,False,now,sample(now))
        self.assertTrue(state['allowed'])
        self.assertFalse(assess(self.root,state,False,200,sample(200))['allowed'])
        self.assertFalse(assess(self.root,state,False,215,{})['allowed'])
        self.assertNotIn('fault',state)
        for now in range(230,351,15):assess(self.root,state,False,now,sample(now))
        self.assertTrue(state['allowed']);self.assertNotIn('error',state)

    def test_policy_validation_is_bounded_and_missing_policy_is_opt_out(self):
        for key,value in (('activeFraction',0),('pauseCpuPercent',10),('idleSeconds',float('nan')),('enabled','true')):
            (self.root/'resource-policy.json').write_text(json.dumps(dict(DEFAULTS,**{key:value})))
            with self.assertRaises(ValueError):policy(self.root)
            self.assertFalse(assess(self.root,{},True,0,sample())['allowed'])
        (self.root/'resource-policy.json').unlink()
        self.assertTrue(assess(self.root,{},False)['allowed'])

    def test_swap_parser_counts_current_io_counters_not_old_swap_occupancy(self):
        output='Mach Virtual Memory Statistics: (page size of 16384 bytes)\nSwapins: 10.\nSwapouts: 20.\n'
        self.assertEqual(parse_swap(output),30*16384)
        with self.assertRaises(ValueError):parse_swap('unknown')

    def test_probe_caching_avoids_extra_subprocesses(self):
        state={}
        with patch('resource_policy.sample_host',return_value=sample()) as probe:
            assess(self.root,state,False,0);assess(self.root,state,False,2)
            probe.assert_called_once()
            assess(self.root,state,False,10);self.assertEqual(probe.call_count,2)

    def test_pacing_leaves_three_idle_intervals_for_one_work_interval_and_bounds_sleep(self):
        clock=Mock(return_value=0);sleep=Mock()
        budget=WorkBudget(self.root,clock,sleep)
        clock.return_value=.1;budget.pace()
        self.assertAlmostEqual(sleep.call_args.args[0],.3)
        clock.return_value=100;budget.pace()
        self.assertEqual(sleep.call_args.args[0],2)

    @unittest.skipUnless(sys.platform=='darwin','macOS process policies')
    def test_real_compute_child_and_grandchild_inherit_low_cpu_and_passive_io(self):
        script='''import ctypes,json,os,subprocess,sys
from resource_policy import lower_compute_priority
lower_compute_priority(sys.argv[1])
probe="import ctypes,json,os; print(json.dumps([os.getpriority(os.PRIO_PROCESS,0),ctypes.CDLL(None).getiopolicy_np(0,0)]))"
print(subprocess.check_output([sys.executable,'-c',probe],text=True).strip())
'''
        result=subprocess.check_output([sys.executable,'-c',script,str(self.root)],
                                       cwd=Path(__file__).parent,text=True,timeout=15)
        self.assertEqual(json.loads(result),[19,2])

    def test_capacity_stop_verifies_save_before_resuming_same_checkpoint_command(self):
        run=self.root/'runs/saved';run.mkdir(parents=True)
        command=['python','supervisor.py','--root',str(self.root),'--run-id','saved','--resume']
        config=dict(root=str(self.root),runId='saved',command=command,enabled=True)
        (self.root/'supervisor.lock').write_text('12')
        processes={12:command};signals=[];launches=[]
        state=tick(config,{},True,80,0,processes.get,signals.append,launches.append,resources=sample(memory=2))
        self.assertEqual(state['state'],'saving');self.assertIn('memory_pressure',state['stopReason'])
        self.assertEqual(signals,[12]);self.assertFalse(launches)
        state=tick(config,state,True,80,15,processes.get,signals.append,launches.append,resources=sample(15))
        self.assertEqual(state['state'],'saving');self.assertFalse(launches)
        processes.clear();(self.root/'supervisor.lock').unlink()
        (run/'supervisor-status.json').write_text(json.dumps(dict(state='stopped',exitCode=0)))
        for now in range(30,151,15):
            state=tick(config,state,True,80,now,processes.get,signals.append,
                       lambda cmd: launches.append(cmd) or 13,resources=sample(now))
        self.assertEqual(state['state'],'starting');self.assertEqual(launches,[command])

    def test_supervisor_resource_stop_is_adopted_but_wrong_pid_and_failed_saves_are_not(self):
        run=self.root/'runs/saved';run.mkdir(parents=True)
        command=['python','supervisor.py','--root',str(self.root),'--run-id','saved','--resume']
        config=dict(root=str(self.root),runId='saved',command=command,enabled=True)
        for pid,code,expected in ((12,0,'waiting_for_resources'),(13,0,'fault'),(12,1,'fault')):
            (run/'supervisor-status.json').write_text(json.dumps(dict(supervisorPid=pid,state='stopped',
                exitCode=code,stopReason='host_resources')))
            state=tick(config,dict(lastPid=12),True,80,0,lambda pid: '',resources=sample(memory=2))
            self.assertEqual(state['state'],expected)


if __name__=='__main__':unittest.main()
