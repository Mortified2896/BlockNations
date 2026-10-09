"""Preserve SDK failure detection while allowing a live human/paused arena to wait."""
import inspect
import json
from pathlib import Path
import time


def waiting_for_human(path: Path | None) -> bool:
    if path is None:
        return False
    try:
        if time.time() - path.stat().st_mtime > 10:
            return False  # A frozen/dead player must still time out.
        state = json.loads(path.read_text())
        return bool(state.get("trainerConnected") and not state.get("failure") and
                    (state.get("paused") or state.get("humanPlaytest")))
    except (OSError, ValueError):
        return False


def install(learn):
    original_factory = learn.create_environment_factory

    def factory(*args, **kwargs):
        arguments = inspect.signature(original_factory).bind(*args, **kwargs).arguments.get("env_args") or []
        status_path = None
        if "--training-status" in arguments:
            status_path = Path(arguments[arguments.index("--training-status") + 1])
        original_environment = original_factory(*args, **kwargs)

        def environment(worker_id, side_channels):
            # This executes inside the SDK's spawned environment worker, not just
            # in its parent. Keep the installed SDK files unchanged.
            from mlagents_envs.environment import UnityEnvironment
            from mlagents_envs.rpc_communicator import RpcCommunicator
            from mlagents_envs.exception import UnityTimeOutException

            class InteractiveCommunicator(RpcCommunicator):
                def poll_for_timeout(self, poll_callback=None):
                    active_wait = 0.0
                    while active_wait < self.timeout_wait:
                        before = time.monotonic()
                        if self.unity_to_external.parent_conn.poll(min(.5, self.timeout_wait - active_wait)):
                            return
                        if poll_callback:
                            poll_callback()  # Native process death still fails immediately.
                        if not waiting_for_human(status_path):
                            active_wait += time.monotonic() - before
                    raise UnityTimeOutException("The Unity arena stopped responding outside a live human turn or pause.")

            original_communicator = UnityEnvironment._get_communicator
            UnityEnvironment._get_communicator = staticmethod(InteractiveCommunicator)
            try:
                return original_environment(worker_id, side_channels)
            finally:
                UnityEnvironment._get_communicator = staticmethod(original_communicator)

        return environment

    learn.create_environment_factory = factory
