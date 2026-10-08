"""Launch limits and the continuing-learning plan, independent of the simulator."""
from copy import deepcopy
from dataclasses import dataclass
import math

# ML-Agents 1.1.0 requires a finite integer. This exact, float-representable
# SDK bookkeeping ceiling is billions of years away at the measured throughput.
CONTINUOUS_STEPS = 1 << 62
BEHAVIOR = "BlockNationsSeatV2"


@dataclass(frozen=True)
class RunLimits:
    hours: float
    budget_gb: float
    free_gb: float = 20
    reserve_mb: int = 512

    def __post_init__(self):
        if (not all(math.isfinite(value) for value in (self.hours, self.budget_gb, self.free_gb))
                or self.hours < 0 or self.free_gb < 0 or self.reserve_mb < 1
                or self.budget_bytes <= self.reserve_bytes):
            raise ValueError("Hours must be finite and nonnegative (0 = no time limit); storage must exceed the checkpoint reserve.")

    @property
    def budget_bytes(self):
        return int(self.budget_gb * 1_000_000_000)

    @property
    def reserve_bytes(self):
        return self.reserve_mb * 1_000_000

    @property
    def duration_seconds(self):
        return self.hours * 3600

    def stop_reason(self, elapsed, used_bytes, free_bytes):
        if self.hours > 0 and elapsed >= self.duration_seconds:
            return "duration_limit"
        if used_bytes >= self.budget_bytes - self.reserve_bytes:
            return "artifact_budget"
        if free_bytes < self.free_gb * 1_000_000_000 + self.reserve_bytes:
            return "free_disk_guard"
        return None


def is_continuous(config):
    return config.get("behaviors", {}).get(BEHAVIOR, {}).get("max_steps") == CONTINUOUS_STEPS


def continuous_config(config):
    """Keep the network/rewards/optimizer settings; remove endpoint annealing."""
    result = deepcopy(config)
    behavior = result.get("behaviors", {}).get(BEHAVIOR)
    if not behavior or behavior.get("trainer_type") != "ppo":
        raise ValueError("Continuing learning requires the owned BlockNationsSeatV2 PPO configuration.")
    behavior["max_steps"] = CONTINUOUS_STEPS
    parameters = behavior["hyperparameters"]
    for key in ("learning_rate_schedule", "beta_schedule", "epsilon_schedule"):
        parameters[key] = "constant"
    return result
