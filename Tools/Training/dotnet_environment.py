"""Batched, viewer-independent C# environment for the pinned ML-Agents trainer.

Uses the public BaseEnv/EnvManager contracts. The stock PPO and self-play policy
logic remain unchanged; no installed SDK files are edited.
"""
import base64
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import threading
import time

import numpy as np
from mlagents_envs.base_env import (BaseEnv, BehaviorSpec, ObservationSpec, DimensionProperty,
                                  ObservationType, ActionSpec, ActionTuple, DecisionSteps, TerminalSteps)
from mlagents.trainers.env_manager import EnvManager, EnvironmentStep
from training_contract import RULES_VERSION, OBSERVATIONS, ACTIONS, OPENING_ECONOMY_VERSION, validate_opening_economy
from training_opponents import TrainingOpponents

BEHAVIOR = 'BlockNationsSeatV2'
NAMES = [BEHAVIOR + '?team=' + str(seat) for seat in (0, 1)]
SPEC = BehaviorSpec([ObservationSpec((OBSERVATIONS,), (DimensionProperty.NONE,), ObservationType.DEFAULT, 'VectorSensor')],
                    ActionSpec.create_discrete((ACTIONS,)))


def atomic_json(path, data):
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(data) + '\n')
    temporary.replace(path)


class DotNetEnvironment(BaseEnv):
    def __init__(self, executable, run, workers, seed, size, curriculum, distance, session, policy_provider=lambda: None,
                 opening_economy_version=OPENING_ECONOMY_VERSION, first_seat=-1):
        opening_economy_version = validate_opening_economy(opening_economy_version)
        if type(first_seat) is not int or first_seat not in (-1, 0, 1):
            raise ValueError('Choose a random starter or explicit seat 0/1 for evaluation.')
        # The worker uses this directory as cwd; passing a relative --run again
        # would put its journals/replays in a nested copy of the directory.
        self.run, self.workers = Path(run).absolute(), workers
        self.policy_provider = policy_provider
        self.opponents = TrainingOpponents(self.run, seed)
        self.actions, self.results, self.stats = {}, {}, []
        self.closed = False
        self.failure = ''
        self.next_status = 0
        self.started = time.monotonic()
        self.responses = queue.Queue(maxsize=2)
        self.stderr_tail = ''
        executable = Path(executable).absolute()
        if not executable.is_file() or workers not in (1, 2, 4, 8, 16):
            raise ValueError('Choose a built C# worker and a supported parallel-game count.')
        prefix = [str(executable)]
        if executable.suffix == '.dll':
            dotnet = os.environ.get('BLOCKNATIONS_DOTNET') or shutil.which('dotnet')
            if not dotnet:
                raise ValueError('The dotnet runtime is missing; configure its executable or build a self-contained worker.')
            prefix = [dotnet, str(executable)]
        command = prefix + ['--run', str(self.run), '--workers', str(workers), '--seed', str(seed),
                            '--size', str(size), '--curriculum', str(bool(curriculum)).lower(), '--distance', str(distance), '--session', session,
                            '--opening-economy-version', str(opening_economy_version), '--first-seat', str(first_seat)]
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True, bufsize=1, cwd=self.run)
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()
        threading.Thread(target=self._read_errors, daemon=True).start()

    def _read(self):
        try:
            while not self.closed:
                line = self.process.stdout.readline(2_000_001)
                if not line:
                    self.responses.put(RuntimeError('C# simulation exited unexpectedly.'))
                    return
                if len(line) > 2_000_000 or not line.endswith('\n'):
                    raise ValueError('Oversized or incomplete C# response.')
                self.responses.put(json.loads(line))
        except Exception as error:
            if not self.closed:
                self.responses.put(error)

    def _read_errors(self):
        for line in self.process.stderr:
            self.stderr_tail = (self.stderr_tail + line)[-8192:]

    @property
    def behavior_specs(self):
        return {name: SPEC for name in NAMES}

    def _exchange(self, request):
        if self.closed:
            raise RuntimeError('Simulation is closed.')
        try:
            self.process.stdin.write(json.dumps(request, separators=(',', ':')) + '\n')
            self.process.stdin.flush()
            response = self.responses.get(timeout=30)
        except (BrokenPipeError, queue.Empty) as error:
            self.failure = 'C# simulation stopped responding. ' + self.stderr_tail
            self._publish_status(force=True, failure=self.failure)
            raise RuntimeError('C# simulation stopped responding. ' + self.stderr_tail) from error
        if isinstance(response, Exception):
            self.failure = str(response) + ' ' + self.stderr_tail
            self._publish_status(force=True, failure=self.failure)
            raise RuntimeError(str(response) + ' ' + self.stderr_tail) from response
        if (response.get('protocol') != 1 or response.get('behavior') != BEHAVIOR or
                response.get('observationSize') != OBSERVATIONS or response.get('actionCount') != ACTIONS or
                response.get('rulesVersion') != RULES_VERSION):
            raise ValueError('C# rules/model protocol mismatch; do not train on an incompatible worker.')
        self.stats = response['stats']
        self.roster = tuple(response.get('recruitTypes', ()))
        if len(self.stats) != self.workers:
            raise ValueError('Worker count changed unexpectedly.')
        if request['op'] == 'status':
            self._publish_status(paused=True)
            return  # Telemetry only: retain the previous decision agents/tensors.
        if request['op'] == 'reference':
            actions = {item['agent']: item['action'] for item in response['referenceActions']}
            decisions, _ = self.get_steps(NAMES[request['referenceSeat']])
            for agent, choice in actions.items():
                if agent not in decisions.agent_id_to_index or not 0 <= choice < ACTIONS or decisions.action_mask[0][decisions.agent_id_to_index[agent], choice]:
                    raise ValueError('Reference produced a stale or masked choice.')
            return actions  # Read-only evaluation advice never advances a game.
        self.results = {}
        for seat, name in enumerate(NAMES):
            decisions = [item for item in response['steps'] if item['seat'] == seat and not item['terminal']]
            terminal = [item for item in response['steps'] if item['seat'] == seat and item['terminal']]
            def observations(items):
                values = [np.frombuffer(base64.b64decode(item['observation'], validate=True), dtype='<f4') for item in items]
                if any(value.shape != (OBSERVATIONS,) or not np.isfinite(value).all() for value in values):
                    raise ValueError('Invalid policy tensor.')
                return [np.stack(values) if values else np.empty((0, OBSERVATIONS), dtype=np.float32)]
            def identifiers(items):
                ids = np.array([item['agent'] for item in items], dtype=np.int32)
                if len(set(ids)) != len(ids) or any(i < 0 or i >= self.workers * 2 or i % 2 != seat for i in ids):
                    raise ValueError('Invalid agent/seat assignment.')
                return ids
            mask = np.ones((len(decisions), ACTIONS), dtype=np.bool_)
            for row, item in enumerate(decisions):
                available = item['available']
                if not available or any(action < 0 or action >= ACTIONS for action in available):
                    raise ValueError('Invalid legal-action mask.')
                mask[row, available] = False
            self.results[name] = (
                DecisionSteps(observations(decisions), np.zeros(len(decisions), dtype=np.float32), identifiers(decisions), [mask],
                              np.zeros(len(decisions), dtype=np.int32), np.zeros(len(decisions), dtype=np.float32)),
                TerminalSteps(observations(terminal), np.array([item['reward'] for item in terminal], dtype=np.float32),
                              np.array([item['interrupted'] for item in terminal], dtype=np.bool_), identifiers(terminal),
                              np.zeros(len(terminal), dtype=np.int32), np.zeros(len(terminal), dtype=np.float32)))
        self._publish_status()

    def _publish_status(self, paused=False, force=False, failure=''):
        now = time.monotonic()
        if not self.stats or (not force and now < self.next_status):
            return
        self.next_status = now + 1
        status = dict(self.stats[0])
        for key in ('decisions', 'actions', 'games', 'captures', 'interruptions', 'rejections', 'trainerResets'):
            status[key] = sum(worker[key] for worker in self.stats)
        status.update(paused=paused, trainerConnected=not self.closed, failure=failure, humanPlaytest=False,
                      workerCount=self.workers, elapsedSeconds=now-self.started,
                      challengerDecisions=self.opponents.decisions, challengerPolicy=self.opponents.identifier,
                      decisionsPerSecond=status['decisions']/max(.001, now-self.started), updatedUnix=time.time())
        atomic_json(self.run / 'arena-status.json', status)

    def reset(self):
        self.actions.clear()
        self._exchange({'op': 'reset'})

    def reference_actions(self, seat, recruit_type='', work_budget=64, agents=None):
        request = dict(op='reference', referenceSeat=seat, recruitType=recruit_type, workBudget=work_budget)
        if agents is not None:
            request['referenceAgents'] = list(agents)
        return self._exchange(request)

    def step(self):
        next_snapshot = 0
        while True:
            try:
                paused = json.loads((self.run / 'training-control.json').read_text()).get('paused', False)
            except FileNotFoundError:
                paused = False
            if type(paused) is not bool:
                raise ValueError('Training pause must be a boolean.')
            if not paused:
                break
            if self.process.poll() is not None:
                raise RuntimeError('Simulation died while training was paused.')
            if time.monotonic() >= next_snapshot:
                self._exchange({'op': 'status'})
                next_snapshot = time.monotonic() + 1
            self._publish_status(paused=True)
            time.sleep(.1)
        policy = self.policy_provider()
        actions, assignments = self.opponents.prepare(self, self.actions, policy)
        request = {'op': 'step', 'actions': [{'agent': agent, 'action': action} for agent, action in actions.items()], 'policy': policy}
        if assignments is not None:
            request['policies'] = assignments
        self._exchange(request)
        self.actions.clear()

    def set_actions(self, behavior_name, action):
        decisions, _ = self.get_steps(behavior_name)
        if len(decisions) == 0:
            if isinstance(action, ActionTuple) and len(action.discrete):
                raise ValueError('Actions supplied for a behavior with no decision agents.')
            return  # SDK policies return an empty list for an inactive seat.
        if action.discrete.shape != (len(decisions), 1):
            raise ValueError('Action dimensions differ from the announced agents.')
        for row, agent in enumerate(decisions.agent_id):
            self.set_action_for_agent(behavior_name, int(agent), ActionTuple(discrete=action.discrete[row:row+1]))

    def set_action_for_agent(self, behavior_name, agent_id, action):
        decisions, _ = self.get_steps(behavior_name)
        if agent_id not in decisions.agent_id_to_index or agent_id in self.actions or action.discrete.shape != (1, 1):
            raise ValueError('Duplicate, stale or non-owning agent action.')
        choice = int(action.discrete[0, 0])
        if choice < 0 or choice >= ACTIONS or decisions.action_mask[0][decisions.agent_id_to_index[agent_id], choice]:
            raise ValueError('Policy chose a masked action.')
        self.actions[agent_id] = choice

    def get_steps(self, behavior_name):
        if behavior_name not in self.behavior_specs:
            raise KeyError(behavior_name)
        return self.results.get(behavior_name, (DecisionSteps.empty(SPEC), TerminalSteps.empty(SPEC)))

    def close(self):
        if self.closed:
            return
        self.closed = True
        try:
            if self.process.poll() is None:
                self.process.stdin.write('{"op":"close"}\n')
                self.process.stdin.flush()
                try:
                    self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.process.terminate()
                    self.process.wait(timeout=5)
        finally:
            if self.process.poll() is None:
                self.process.kill(); self.process.wait()
            for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
                stream.close()
            self._publish_status(force=True, failure=self.failure)


class BatchedSimulationManager(EnvManager):
    """Synchronous batched policy inference; environment stepping is outside Unity.

    A bounded native request timeout and explicit process ownership replace the
    SDK Unity worker lifecycle. Failures propagate to the existing save supervisor.
    """
    def __init__(self, factory, options, n_env=1):
        if n_env != 1 or options.environment_parameters:
            raise ValueError('Use the parallel-game setting for this backend; SDK environment randomization needs a separate adapter.')
        super().__init__()
        self.env = factory(0, [])
        self.previous_step = EnvironmentStep.empty(0)

    def _results(self):
        return {name: self.env.get_steps(name) for name in self.env.behavior_specs}

    def _step(self):
        info = {name: self.policies[name].get_action(result[0], 0) for name, result in self.previous_step.current_all_step_result.items()}
        for name, action in info.items():
            self.env.set_actions(name, action.env_action)
        self.env.step()
        self.previous_step = EnvironmentStep(self._results(), 0, info, {})
        return [self.previous_step]

    def _reset_env(self, config=None):
        self.set_env_parameters(config)
        self.env.reset()
        self.previous_step = EnvironmentStep(self._results(), 0, {}, {})
        return [self.previous_step]

    def set_env_parameters(self, config=None):
        if config:
            raise ValueError('Unexpected environment parameter update.')

    @property
    def training_behaviors(self):
        return self.env.behavior_specs

    def close(self):
        self.env.close()


def install(learn, policy_provider):
    if not os.environ.get('BLOCKNATIONS_SIMULATION_WORKER'):
        return False
    def factory(*args, **kwargs):
        def create(worker_id, channels):
            if worker_id != 0:
                raise ValueError('Multiple process workers require separately assigned journals.')
            return DotNetEnvironment(os.environ['BLOCKNATIONS_SIMULATION_WORKER'], os.environ['BLOCKNATIONS_RATING_RUN'],
                                     int(os.environ['BLOCKNATIONS_PARALLEL_GAMES']), int(os.environ['BLOCKNATIONS_SIMULATION_SEED']),
                                     int(os.environ['BLOCKNATIONS_RATING_BOARD']), os.environ['BLOCKNATIONS_SIMULATION_CURRICULUM'] == 'true',
                                     int(os.environ['BLOCKNATIONS_SIMULATION_DISTANCE']), os.environ['BLOCKNATIONS_RATING_SESSION'], policy_provider,
                                     opening_economy_version=int(os.environ.get('BLOCKNATIONS_OPENING_ECONOMY_VERSION', OPENING_ECONOMY_VERSION)))
        return create
    learn.create_environment_factory = factory
    learn.SubprocessEnvManager = BatchedSimulationManager
    return True
