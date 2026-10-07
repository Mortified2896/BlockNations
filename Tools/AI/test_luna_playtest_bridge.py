import json
from pathlib import Path
import subprocess
import threading
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request

import luna_playtest_bridge as bridge


def observation():
    return {"ObservationSchemaVersion": 2, "RulesVersion": bridge.RULES_VERSION,
            "PrivateTransportData": "must-not-enter-the-prompt",
            "Observation": {"Width": 2, "Height": 2, "Seat": 1, "Round": 1, "Gold": 2,
                            "Tiles": [True] * 4, "Seen": [True, False, True, False], "Visible": [True, False, True, False],
                            "Units": [], "RecruitTypes": [], "Cities": [], "HostileSeats": [0],
                            "LegalActions": [{"Kind": 0, "Actor": 0, "Destination": 1, "MoveCost": 1}, {"Kind": 3}]}}


class InputTests(unittest.TestCase):
    def test_all_legal_actions_and_masks_are_preserved_but_extra_data_is_excluded(self):
        view, count = bridge.decision_input(observation())
        self.assertEqual(count, 2)
        self.assertEqual([a["ActionId"] for a in view["LegalActions"]], [0, 1])
        self.assertEqual(view["SeenPositions"], [0, 2])
        self.assertNotIn("must-not-enter-the-prompt", json.dumps(view))

    def test_incompatible_schema_is_rejected(self):
        body = observation()
        body["ObservationSchemaVersion"] = 99
        with self.assertRaises(ValueError):
            bridge.decision_input(body)

    def test_model_and_effort_must_be_a_supported_catalog_combination(self):
        catalog = [{"Id": "gpt-6-luna", "ReasoningEfforts": ["low", "max"]}]
        self.assertEqual(bridge.model_settings({}, catalog), ("gpt-6-luna", "max"))
        for body in ({"Model": "unknown"}, {"ReasoningEffort": "ultra"}):
            with self.assertRaises(ValueError):
                bridge.model_settings(body, catalog)

    def test_codex_invocation_is_isolated_and_requests_the_exact_model_and_effort(self):
        def completed(command, **kwargs):
            self.assertIn("--ignore-user-config", command)
            self.assertIn("--ephemeral", command)
            self.assertEqual(command[command.index("--model") + 1], "gpt-6-luna")
            self.assertIn('model_reasoning_effort="max"', command)
            for feature in bridge.DISABLED_FEATURES:
                self.assertIn(feature, command)
            schema = json.loads(Path(command[command.index("--output-schema") + 1]).read_text())
            self.assertEqual(schema["properties"]["ActionId"]["enum"], [0, 1])
            Path(command[command.index("--output-last-message") + 1]).write_text(json.dumps({"ActionId": 1, "Summary": "Done."}))
            class Completed:
                returncode = 0
                def communicate(child, prompt, timeout):
                    self.assertNotIn("must-not-enter-the-prompt", prompt)
                    return json.dumps({"type": "turn.completed", "usage": {"input_tokens": 120, "output_tokens": 50}}), ""
            return Completed()
        with patch.object(bridge.subprocess, "Popen", side_effect=completed):
            answer = bridge.run_decision("codex", observation(), 30)
        self.assertTrue(answer["HasAction"])
        self.assertEqual(answer["ActionId"], 1)
        self.assertEqual(answer["InputTokens"], 120)


class LocalServerTests(unittest.TestCase):
    def setUp(self):
        self.server = bridge.Bridge(("127.0.0.1", 0), "codex", 1, 5)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = "http://127.0.0.1:" + str(self.server.server_port) + "/decision"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def request(self, body, origin=None):
        headers = {"Content-Type": "application/json"}
        if origin:
            headers["Origin"] = origin
        request = urllib.request.Request(self.url, json.dumps(body).encode(), headers)
        try:
            with urllib.request.urlopen(request, timeout=2) as response:
                return response.status, json.load(response)
        except urllib.error.HTTPError as error:
            return error.code, json.load(error)

    def test_cross_origin_request_cannot_spend_account_quota(self):
        self.assertEqual(self.request(observation(), "https://example.com")[0], 403)
        self.assertEqual(self.server.calls, 0)

    def test_invalid_request_does_not_spend_a_call(self):
        body = observation()
        body["RulesVersion"] = "different"
        self.assertEqual(self.request(body)[0], 400)
        self.assertEqual(self.server.calls, 0)

    def test_session_limit_is_enforced_without_silent_retries(self):
        with patch.object(bridge, "run_decision", return_value={"HasAction": True, "ActionId": 0} ) as call:
            self.assertEqual(self.request(observation())[0], 200)
            status, answer = self.request(observation())
            self.assertEqual(status, 429)
            self.assertEqual(answer["CallsRemaining"], 0)
            self.assertEqual(call.call_count, 1)


if __name__ == "__main__":
    unittest.main()
