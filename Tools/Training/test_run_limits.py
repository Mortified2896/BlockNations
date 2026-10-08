from copy import deepcopy
import unittest

from run_limits import RunLimits, CONTINUOUS_STEPS, continuous_config, is_continuous
from supervisor import trainer_config


class RunLimitTests(unittest.TestCase):
    def test_zero_hours_has_no_timer_but_still_stops_before_using_the_save_reserve(self):
        limits = RunLimits(0, 20)
        self.assertEqual(limits.duration_seconds, 0)
        self.assertIsNone(limits.stop_reason(365 * 24 * 3600, 1_000_000_000, 30_000_000_000))
        self.assertEqual(limits.stop_reason(365 * 24 * 3600, 19_488_000_000, 30_000_000_000), "artifact_budget")
        self.assertEqual(limits.stop_reason(365 * 24 * 3600, 1_000_000_000, 20_511_999_999), "free_disk_guard")

    def test_positive_hours_stop_on_time_and_keep_the_same_resource_guards(self):
        limits = RunLimits(8, 20)
        self.assertIsNone(limits.stop_reason(8 * 3600 - 1, 0, 30_000_000_000))
        self.assertEqual(limits.stop_reason(8 * 3600, 0, 30_000_000_000), "duration_limit")
        self.assertEqual(limits.stop_reason(1, 19_488_000_000, 30_000_000_000), "artifact_budget")
        self.assertEqual(limits.stop_reason(1, 0, 20_511_999_999), "free_disk_guard")

    def test_invalid_or_nonfinite_limits_are_rejected(self):
        for hours in (-1, float("nan"), float("inf"), -float("inf")):
            with self.subTest(hours=hours), self.assertRaises(ValueError):
                RunLimits(hours, 20)
        for budget in (0, .512, float("nan"), float("inf")):
            with self.subTest(budget=budget), self.assertRaises(ValueError):
                RunLimits(0, budget)

    def test_continuing_plan_preserves_the_training_contract_and_original_configuration(self):
        original = trainer_config(1_000_000, 5000)
        before = deepcopy(original)
        result = continuous_config(original)
        self.assertEqual(original, before)
        self.assertTrue(is_continuous(result))
        self.assertFalse(is_continuous(original))
        expected = deepcopy(original)
        behavior = expected["behaviors"]["BlockNationsSeatV2"]
        behavior["max_steps"] = CONTINUOUS_STEPS
        for key in ("learning_rate_schedule", "beta_schedule", "epsilon_schedule"):
            behavior["hyperparameters"][key] = "constant"
        self.assertEqual(result, expected)
        self.assertEqual(continuous_config(result), result)

    def test_actual_sdk_keeps_training_and_learning_past_the_old_step_endpoint(self):
        # Use the installed trainer's real configuration parser, training predicate,
        # and optimizer schedules; checking only our JSON would miss SDK coercion.
        from mlagents.trainers import learn
        from mlagents.plugins.trainer_type import register_trainer_plugins
        from mlagents.trainers.settings import TrainerSettings, ScheduleType
        from mlagents.trainers.trainer.trainer import Trainer
        from mlagents.trainers.torch_entities.utils import ModelUtils

        register_trainer_plugins()
        config = continuous_config(trainer_config(1_000_000, 5000))
        settings = TrainerSettings.structure(config["behaviors"]["BlockNationsSeatV2"], TrainerSettings)
        self.assertEqual(settings.max_steps, CONTINUOUS_STEPS)

        class Probe:
            is_training = True
            get_step = 1_000_001
            get_max_steps = settings.max_steps

        self.assertTrue(Trainer.should_still_train.__get__(Probe()))
        parameters = settings.hyperparameters
        for name, initial, minimum in (("learning_rate", parameters.learning_rate, 1e-10),
                                        ("beta", parameters.beta, 1e-5), ("epsilon", parameters.epsilon, .1)):
            schedule = getattr(parameters, name + "_schedule")
            self.assertEqual(schedule, ScheduleType.CONSTANT)
            value = ModelUtils.DecayedValue(schedule, initial, minimum, settings.max_steps)
            self.assertEqual(value.get_value(1_000_001), initial)
            self.assertEqual(value.get_value(100_000_000), initial)


if __name__ == "__main__":
    unittest.main()
