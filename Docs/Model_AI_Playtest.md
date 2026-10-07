# Model AI local playtest

Implemented 2026-10-07 at the owner's request. **Play vs Model AI** is a separate development menu entry, alongside regular **Play vs AI**. Normal/Default, Rider Focus, and Hard remain in the regular AI selector. The model setup contains board size, model, and reasoning selectors; it does not mix tournament or legacy policy controls into that flow.

## Starting a playtest

1. Open the project with Unity **6000.4.0f1** and use the existing signed-in Codex app.
2. Choose **Tools → Block Nations → Luna Bridge → Start (64 calls)**. The bridge listens only on `127.0.0.1:8769` and stops accepting decisions after its session limit.
3. Enter Play Mode in `Assets/Scenes/MainMenu.unity`. Select **Play vs Model AI**, or use **Tools → Block Nations → Prepare Luna Playtest** to stage 11×11 and maximize the Game view. The helper does not press Start Game.
4. Leave the defaults **GPT-6-Luna / Maximum** for the first review, or select a listed model and one of its supported reasoning levels. Start Game becomes available once the bridge's model catalogue loads with calls remaining.
5. Open **Window → Block Nations → Hard AI Inspector** to see the current fair observation's legal mask, selected action, short model explanation, model/reasoning pair, elapsed time, token use, and remaining session calls. The Inspector's name is retained for this shared development tool.
6. Use **Luna Bridge → Stop** when finished. To reset an exhausted call allocation, stop and explicitly start a new bounded session. Closing Unity stops its owned bridge and active inference process.

The selectors use typed catalogue IDs and supported reasoning values, not rendered strings as gameplay inputs. The settings are transient to the local session; they are not new fields in gameplay saves, PlayerPrefs, or PBp snapshots. Resuming a local model game after restarting the Editor uses the current/default model settings. A release build maps the development-only policy value to Hard instead of attempting a loopback request.

## Decision and fairness contract

`LocalLunaPlaytestPolicy` uses the shared Unity adapter to capture observation schema 2 and the `blocknations-actions-v1` rules version. The request contains the acting seat's own units/cities/resources, currently visible enemy units, last-observed city memory with current/stale flags, public unit statistics, board visibility masks, and **every action in the current authoritative legal mask**. It contains no PBp identities or credentials. Existing Normal/Rider controls retain their earlier visibility behavior separately.

The bridge supplies generic game rules and asks for exactly one legal action ID plus a short player-facing explanation. IDs and unit/city indices belong to that observation. The output schema restricts the ID to the supplied mask; the policy checks it again, and `LegalActionService` revalidates it immediately before execution. After the action, the adapter captures new information and requests the next action. This is not a batch plan and does not blend Hard's search into a model choice. If EndTurn is the only legal action, the runtime can take that forced action without a model call.

Loading another match or disabling the manager cancels pending Unity requests, and obsolete responses cannot execute on replacement state. Transport failures, invalid responses, timeouts, and exhausted quota stop that AI turn and appear in the Inspector/Console; there are no automatic retries or silent switches to a different opponent. The first small playtest can therefore evaluate the selected model itself. A public release needs an explicit in-game recovery flow.

The bridge binds only to loopback, accepts local JSON requests, rejects browser Origin headers, limits request size, permits one active inference, and defaults to 64 calls with a 75-second inference timeout. Unity allows 80 seconds for the request. These are development usage bounds, not difficulty settings or response-time promises. Each actionable decision consumes the signed-in Codex allowance; this is not unlimited/free model hosting on Cloudflare.

## Client and validation evidence

The service prefers the current bundled app client over an older standalone installation. On this machine the working executable is `/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex`, version **0.160.1**. The older standalone **0.153.4** listed Luna but rejected actual Luna inference through ChatGPT sign-in. A fresh real call through the bundled client selected the winning capture in an isolated fair position and took **11.416 seconds**. Catalogue discovery alone does not establish model entitlement; only Luna Maximum has been verified with real inference in this playtest.

Each invocation is ephemeral, in an empty temporary directory, with project docs, web search, tools, integrations, plugins, hooks, and memories disabled. The bridge never reads authentication tokens or forwards account credentials to Unity; Codex owns its existing sign-in. The client uses structured JSON output and the subprocess is bounded/cancelled on timeout or bridge shutdown.

**Tools → Block Nations → Luna Bridge → Verify one real Luna action** explicitly runs one isolated live Unity position through TurnManager dispatch, observation, model selection, legal execution, and capture handling. Regular PlayMode regressions skip that real model call. The latest live Unity check passed: Luna selected action 4 (the enemy-city move), the canonical seat-1 path executed it despite a deliberately stale legacy turn boolean, and city ownership/game-over changed correctly. That request took **14.614 seconds**. An earlier real Unity run did not capture and reached forced EndTurn; its result remains in the diagnostic record. Model choices are not deterministic, so the successful rerun does not erase that strength failure. Results are local ignored artifacts under `Logs/Validation/`; this helper does not run an AI-vs-AI tournament. All 7 Python tests pass, covering action-mask preservation, version/model validation, strict client invocation, cross-origin rejection, and session limits. The targeted Unity regressions pass 60 EditMode cases and 15 ordinary PlayMode cases; the extra live check is deliberately ignored in a regular run.

No general playing strength, model difficulty ordering, three-second latency, complete human match, or mobile/browser acceptance is established by a single-position check. Reasoning levels expose actual supported model settings; they are not calibrated difficulty levels. Higher effort may take longer and does not guarantee a stronger move in every position.

## Next integration steps

The engine-independent observation/action/policy boundary is shared by local search and this external opponent. A later Decisions API adapter can consume the same fair state and legal choices, while keeping API credentials on a backend. That service and its latency have not been implemented or measured here. Public browser/mobile support needs an appropriate authenticated remote transport and recovery UI; an HTTPS Cloudflare build cannot use this native local bridge as its production connection.

Generic features, injected evaluators, and optional versioned decision samples provide a path toward offline learned evaluation or imitation. Proper Unity self-play still needs complete reset/step/terminal APIs, episodes/rewards, broader fair observation memory, reproducible varied evaluations, and parity checks against runtime transitions. No learned weights, training loop, or reinforcement-learning environment has been shipped by this playtest.
