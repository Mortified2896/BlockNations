#!/usr/bin/env python3
"""Loopback-only, bounded single-action playtest using the existing Codex sign-in.

No API keys, Unity/PBp saves, repository files, or shell tools are sent to the model.
Start explicitly: python3 Tools/AI/luna_playtest_bridge.py --max-calls 64
"""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODEL = "gpt-6-luna"
EFFORT = "max"
RULES_VERSION = "blocknations-actions-v1"
OBSERVATION_VERSION = 2
MAX_BODY = 512 * 1024
ACTIVE_PROCESS = None
ACTIVE_PROCESS_LOCK = threading.Lock()
REASONING_EFFORTS = ("none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra")
DISABLED_FEATURES = (
    "shell_tool", "unified_exec", "apps", "plugins", "hooks", "browser_use",
    "browser_use_external", "computer_use", "image_generation", "multi_agent",
    "memories", "goals", "view_image", "in_app_browser", "code_mode_host",
    "workspace_dependencies", "remote_plugin", "skill_search", "skill_mcp_dependency_install",
)
RULES = """You are playing Block Nations, a turn-based tile strategy game. Choose exactly
ONE legal action ID from the supplied current mask. Aim to win; do not end the
turn while a useful action remains. Think about both tactics and longer-term
economy, exploration, and defending your cities. You have no game-control tools.
Only the supplied observation is available: enemy units are visible now; enemy
city ownership may be remembered and stale. Unknown tiles/enemy resources are
unknown. Never assume an unseen enemy location is certain.

Coordinates start at (0,0). Adjacent includes diagonals; distance is Chebyshev.
Moves follow an unoccupied path of adjacent existing tiles. MoveCost consumes
movement; a CommittedMove unit gets one movement action with up to MaxMoves range
and then no further movement that turn. Other units may use remaining movement.
Damage = max(0, attacker.Attack - defender.Defense), deterministic, no retaliation.
AttacksUsed and MovesUsed are spent this turn; MaxAttacks/MaxMoves are limits.
AttackAfterMoving=false forbids attacking after moving, but attacking THEN moving
is allowed if movement remains. An attack with Range<=1 advances into the killed
target's tile; a ranged kill does not advance. A unit's Vision reveals tiles after
each action, possibly exposing new legal targets. Cities provide IncomePerCity
gold per own turn. Recruiting costs the supplied unit Cost, requires a clear own
city tile, and is once per city per turn; a recruit can act immediately.
Moving into or advancing after a melee kill onto a hostile city captures it and
wins immediately. Protect your cities against the opponent's next turn, when its
observed units reset their move/attack resources. You may perform multiple actions
in your turn; after your single chosen action you will receive a fresh state and
fresh legal action IDs. IDs are local to this request. EndTurn ends the whole turn.
Unit statistics and legal actions are authoritative; do not invent abilities.
Return ActionId and a short player-facing Summary, not private reasoning steps.
"""


def model_catalog():
    # Public model capabilities only. Never read auth.json or user config.
    cache = Path(os.environ.get("CODEX_HOME", str(Path.home() / ".codex"))) / "models_cache.json"
    try:
        data = json.loads(cache.read_text(encoding="utf-8"))
        models = []
        for model in data.get("models", []):
            slug = model.get("slug", "")
            efforts = [level["effort"] for level in model.get("supported_reasoning_levels", [])
                       if level.get("effort") in REASONING_EFFORTS]
            if model.get("visibility") == "list" and re.fullmatch(r"[a-z0-9][a-z0-9._-]{1,80}", slug) and efforts:
                models.append({"Id": slug, "Name": model.get("display_name", slug), "ReasoningEfforts": efforts})
        if models:
            return models
    except (OSError, ValueError, TypeError, KeyError):
        pass
    return [{"Id": MODEL, "Name": "GPT-6-Luna", "ReasoningEfforts": ["low", "medium", "high", "xhigh", "max"]}]


def model_settings(body, catalog):
    model, effort = body.get("Model", MODEL), body.get("ReasoningEffort", EFFORT)
    for choice in catalog:
        if choice["Id"] == model and effort in choice["ReasoningEfforts"]:
            return model, effort
    raise ValueError("Model or reasoning level is not in the signed-in Codex catalog.")


def find_codex():
    # Prefer the app's current client to an older standalone installation. Model
    # discovery in an old client may list a model that its inference route rejects.
    for candidate in ("/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex",
                      "/Applications/Codex.app/Contents/Resources/codex", shutil.which("codex"),
                      str(Path.home() / ".local/bin/codex")):
        if candidate and os.path.isfile(candidate):
            return candidate
    return "codex"


def inference_failure(stdout, stderr):
    # Report known causes without returning raw diagnostics, URLs, or credentials.
    diagnostic = (stdout + stderr).lower()
    if "not supported when using codex with a chatgpt account" in diagnostic:
        return "This Codex client rejected the selected model for ChatGPT sign-in. Use the current bundled app client with --codex."
    if "unauthorized" in diagnostic or "invalid_refresh_token" in diagnostic or '"status":401' in diagnostic:
        return "Codex sign-in needs attention. Sign in again in Codex, then retry the local playtest."
    if "rate limit" in diagnostic or "usage limit" in diagnostic or '"status":429' in diagnostic:
        return "The signed-in account's model usage limit was reached. Check usage in Codex."
    return "Codex did not complete a decision. Check local sign-in and selected model availability."


def decision_input(body):
    if body.get("ObservationSchemaVersion") != OBSERVATION_VERSION or body.get("RulesVersion") != RULES_VERSION:
        raise ValueError("Observation schema/rules version does not match this bridge.")
    obs = body.get("Observation")
    if not isinstance(obs, dict):
        raise ValueError("Observation is required.")
    width, height = obs.get("Width", 0), obs.get("Height", 0)
    if type(width) is not int or type(height) is not int or not (1 <= width <= 31 and 1 <= height <= 31):
        raise ValueError("Invalid board dimensions.")
    size = width * height
    for key in ("Tiles", "Seen", "Visible"):
        if not isinstance(obs.get(key), list) or len(obs[key]) != size or any(type(v) is not bool for v in obs[key]):
            raise ValueError("Invalid tile mask.")
    actions = obs.get("LegalActions")
    if not isinstance(actions, list) or not actions or len(actions) > 10000:
        raise ValueError("A nonempty legal-action mask is required.")
    unit_keys = ("Seat", "X", "Y", "Type", "Health", "MaxHealth", "Attack", "Defense", "Range", "Vision",
                 "MaxMoves", "MovesUsed", "MaxAttacks", "AttacksUsed", "Cost", "AttackAfterMoving", "CommittedMove")
    def units(key):
        values = obs.get(key)
        if not isinstance(values, list) or len(values) > size * 4:
            raise ValueError("Invalid unit/catalog list.")
        return [{k: u[k] for k in unit_keys if k in u} for u in values]
    view = {k: obs.get(k) for k in ("Width", "Height", "Seat", "Round", "Gold", "CityVision", "IncomePerCity", "HostileSeats")}
    view.update(Units=units("Units"), RecruitTypes=units("RecruitTypes"))
    view["Cities"] = [{k: city.get(k) for k in ("Seat", "X", "Y", "Recruited", "CurrentlyVisible")} for city in obs.get("Cities", [])]
    # Compact masks keep all board information while avoiding 363 repeated booleans.
    for key in ("Tiles", "Seen", "Visible"):
        view[key + "Positions"] = [i for i, exists in enumerate(obs[key]) if exists]
    view["PositionEncoding"] = "position = y * Width + x"
    view["LegalActions"] = []
    kinds = ("Move", "Attack", "Recruit", "EndTurn")
    for i, action in enumerate(actions):
        kind = action.get("Kind")
        if type(kind) is not int or not (0 <= kind < len(kinds)):
            raise ValueError("Invalid legal action kind.")
        item = {"ActionId": i, "Kind": kinds[kind]}
        if kind != 3:
            item.update({k: action.get(k) for k in ("Actor", "Target", "Destination", "MoveCost", "RecruitType")})
        view["LegalActions"].append(item)
    return view, len(actions)


def codex_command(codex, directory, schema, output, model=MODEL, effort=EFFORT):
    command = [codex, "exec", "--ignore-user-config", "--ephemeral", "--skip-git-repo-check",
               "--sandbox", "read-only", "--model", model, "--config", "model_reasoning_effort=" + json.dumps(effort),
               "--config", "project_doc_max_bytes=0", "--config", 'web_search="disabled"',
               "--cd", str(directory), "--output-schema", str(schema), "--output-last-message", str(output),
               "--color", "never", "--json"]
    for feature in DISABLED_FEATURES:
        command.extend(("--disable", feature))
    command.append("-")
    return command


def run_decision(codex, body, timeout):
    global ACTIVE_PROCESS
    view, action_count = decision_input(body)
    model, effort = model_settings(body, model_catalog())
    schema = {"type": "object", "additionalProperties": False, "required": ["ActionId", "Summary"],
              "properties": {"ActionId": {"type": "integer", "enum": list(range(action_count))},
                             "Summary": {"type": "string"}}}
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix="blocknations-luna-") as temporary:
        directory = Path(temporary)
        schema_path, output = directory / "choice.schema.json", directory / "choice.json"
        schema_path.write_text(json.dumps(schema), encoding="utf-8")
        # Existing authentication is read by Codex. Neither credentials nor user
        # config/MCP integrations are read by this service or given to the model.
        with ACTIVE_PROCESS_LOCK:
            process = subprocess.Popen(codex_command(codex, directory, schema_path, output, model, effort),
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            ACTIVE_PROCESS = process
        try:
            stdout, stderr = process.communicate(RULES + "\nCurrent fair observation:\n" + json.dumps(view, separators=(",", ":")), timeout=timeout)
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate()
            raise
        finally:
            with ACTIVE_PROCESS_LOCK:
                ACTIVE_PROCESS = None
        if process.returncode != 0 or not output.is_file():
            raise RuntimeError(inference_failure(stdout, stderr))
        answer = json.loads(output.read_text(encoding="utf-8"))
        action_id = answer.get("ActionId")
        if type(action_id) is not int or not (0 <= action_id < action_count):
            raise RuntimeError("Model returned an invalid legal-action ID.")
        usage = {}
        for line in stdout.splitlines():
            try:
                event = json.loads(line)
            except ValueError:
                continue
            if event.get("type") == "turn.completed":
                usage = event.get("usage", {})
        return {"HasAction": True, "ActionId": action_id, "Summary": str(answer.get("Summary", ""))[:600],
                "Model": model, "ReasoningEffort": effort, "Seconds": round(time.monotonic() - started, 3),
                "InputTokens": usage.get("input_tokens", 0), "OutputTokens": usage.get("output_tokens", 0), "Error": ""}


class Bridge(ThreadingHTTPServer):
    daemon_threads = True
    def __init__(self, address, codex, max_calls, timeout):
        super().__init__(address, Handler)
        self.codex, self.max_calls, self.timeout = codex, max_calls, timeout
        self.calls = 0
        self.active_request = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # Never log observations, authentication, subprocess output, or URLs.

    def send_json(self, status, body):
        data = json.dumps(body).encode("utf-8")
        try:
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_GET(self):
        if self.path == "/models":
            self.send_json(200, {"Models": model_catalog(), "CallsRemaining": max(0, self.server.max_calls - self.server.calls)})
            return
        if self.path != "/health":
            self.send_json(404, {"Error": "Unknown endpoint"})
            return
        self.send_json(200, {"Model": MODEL, "ReasoningEffort": EFFORT,
                             "CallsRemaining": max(0, self.server.max_calls - self.server.calls),
                             "ObservationSchemaVersion": OBSERVATION_VERSION, "RulesVersion": RULES_VERSION})

    def do_POST(self):
        # Cross-origin browser pages cannot spend the owner's signed-in quota.
        if self.path != "/decision" or self.headers.get("Origin") or self.headers.get_content_type() != "application/json":
            self.send_json(403, {"ActionId": -1, "Error": "Only local development JSON requests are accepted."})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= MAX_BODY:
                raise ValueError("Request exceeds the playtest size limit.")
            body = json.loads(self.rfile.read(length))
            decision_input(body)  # Reject invalid input before consuming a call.
            model_settings(body, model_catalog())
        except (ValueError, TypeError, KeyError, AttributeError):
            self.send_json(400, {"ActionId": -1, "Error": "Invalid or incompatible game observation."})
            return
        if not self.server.active_request.acquire(blocking=False):
            self.send_json(429, {"ActionId": -1, "Error": "A Luna decision is already running."})
            return
        try:
            if self.server.calls >= self.server.max_calls:
                self.send_json(429, {"ActionId": -1, "Error": "Local playtest call limit reached. Restart the bridge to approve another bounded session.", "CallsRemaining": 0})
                return
            self.server.calls += 1
            try:
                answer = run_decision(self.server.codex, body, self.server.timeout)
                status = 200
            except subprocess.TimeoutExpired:
                answer, status = {"ActionId": -1, "Error": "Luna decision exceeded the local playtest timeout."}, 504
            except RuntimeError as error:
                answer, status = {"ActionId": -1, "Error": str(error)}, 502
            except (ValueError, OSError):
                answer, status = {"ActionId": -1, "Error": "Codex decision failed. Check the signed-in local CLI and model availability."}, 502
            answer["CallsRemaining"] = max(0, self.server.max_calls - self.server.calls)
            self.send_json(status, answer)
            print(f"[Luna playtest] call {self.server.calls}/{self.server.max_calls}; status={status}; seconds={answer.get('Seconds', 0)}", flush=True)
        finally:
            self.server.active_request.release()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8769)
    parser.add_argument("--codex", default=find_codex())
    parser.add_argument("--max-calls", type=int, default=64)
    parser.add_argument("--timeout", type=int, default=75)
    args = parser.parse_args()
    if not (1 <= args.max_calls <= 256 and 5 <= args.timeout <= 120):
        parser.error("Use 1–256 calls and a 5–120 second per-call timeout.")
    if not os.path.isfile(args.codex):
        parser.error("Codex CLI was not found; pass its executable with --codex.")
    server = Bridge(("127.0.0.1", args.port), args.codex, args.max_calls, args.timeout)
    def stop_service(signum, frame):
        with ACTIVE_PROCESS_LOCK:
            if ACTIVE_PROCESS is not None and ACTIVE_PROCESS.poll() is None:
                ACTIVE_PROCESS.terminate()
        threading.Thread(target=server.shutdown, daemon=True).start()
    signal.signal(signal.SIGTERM, stop_service)
    signal.signal(signal.SIGINT, stop_service)
    print(f"[Luna playtest] http://127.0.0.1:{args.port}; {MODEL}; reasoning={EFFORT}; maximum {args.max_calls} calls. Ctrl-C stops the bridge.", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
