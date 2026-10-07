# Block Nations Development Follow-ups

Phase 1 documentation cleanup was completed on 2026-10-07. It leaves gameplay/server behavior, scene/prefab wiring, serialized assets, credentials, and compatibility support unchanged. The items below are separate work, not completed fixes.

Reviewed: 2026-10-07, against source revision `0d510b3`.

## Relay correctness before online playtesting

**Confirmed issue:** in an isolated temporary copy of `server.js`, concurrent different payloads submitted to the same game/sequence both returned HTTP 200 success. The current check/write/rename path can replace a destination created by another request. Sequential duplicates and conflicts work.

- Likely change boundary: `server.js` and a focused Node regression test; `package.json` only if needed to expose the test.
- Proper solution direction: atomically publish a turn without replacing an existing destination, then compare a competing payload for idempotent success or conflict. Choose the primitive for the actual hosting filesystem/process model.
- Preserve the HTTP payload/response contract and existing stored turns.
- Validate identical/different concurrent submissions, sequential duplicate/conflict, fetch of the winning payload, and restart persistence.
- Fix before reopening the relay to online testers. Phase 1 only documents it.

## Server entry point and deployment

`npm start` uses `server.js`. `server.prod.js` lacks `/pbp/game/claim` and the current `turnSeat` status field.

- Before removing the old copy, inspect the deployed process command/revision and any external deployment scripts.
- Likely boundary after that check: `server.prod.js`, the package lint/check scripts, ESLint config if needed, and deployment documentation.
- Retain one supported implementation after deployment references are reconciled.
- The public endpoint failed TLS negotiation in two unauthenticated checks on 2026-10-07. Inspect TLS/proxy/origin state; do not infer the relay is stopped solely from that result.
- Review distributed mobile playtest credentials and provisioning/rotation before renewed external access. Never copy their values into documentation or reports.
- Verify client-IP rate limiting through the chosen tunnel/proxy. Current Express configuration does not enable `trust proxy`; players may share an origin connection IP. Configure trust only for the actual trusted path.
- Validate fresh-client create/claim/submit/fetch, correct app/protocol rejection, reconnect after a network interruption, and restart persistence.

## Phase 2 AI boundary and improvements

This is recommended core-system work for a separately requested phase 2. Plan before patching; choose exact extraction files during that pass.

- Primary source: `Assets/Scripts/Core/TurnManager.cs`, especially `RunAIForSide` and adjacent recruit/target/move/attack/defense helpers.
- Relevant existing helpers: `AITurnLogic`, `LegalActionService`, `AICityCaptureTacticalPlanner`, and `AIPostCalculusLocalDecisionHelper`.
- Extract AI orchestration/decisions through a clear interface while keeping turn lifecycle and authoritative execution stable. A mechanical split into many files alone does not resolve ownership/state coupling.
- Move new AI paths to explicit acting seats and acting-seat visibility. Preserve viewer POV and existing free-for-all behavior.
- Give ally/enemy relationships an explicit boundary before adding team behavior. Different ownership will not by itself imply hostility when teams exist.
- Retain tactical helpers and batch/tournament/CSV tooling. The active `AIPostCalculusLocalDecisionHelper` name can be clarified later; it is not dead Calculus runtime code.
- The capture planner has a 250 ms search budget. Measure device frame impact before increasing search scope or depth.
- Validate both AI seats, visibility loss after movement, city capture/defense, legal recruitment/action execution, repeatable evaluation conditions, and mobile turn responsiveness. Then improve tactics in a separate behavior-changing step.

## Players and teams

The user's near-term direction is **4 or 5 players**, without teams initially. Current PBp implementation supports **2–4**, and both Unity's `PlayByPostSeatUtility` and the relay clamp that range. Neither is a permanent architectural target.

Preserve independent player identity, match seats/ownership, team membership/relationships, turn ownership, viewer visibility, and transport state. Joining/leaving and team switching are intended capabilities; their rules are not settled.

Before implementing capacity or membership changes, decide:

- A practical initial limit, slot allocation, and whether seats remain stable after departure/replacement.
- New participant admission versus reconnecting an existing identity or claiming an unclaimed slot.
- What resignation/departure/elimination does to units, cities, turn order, and victory.
- How team changes affect hostility, shared vision/resources, friendly fire, victory, and current actions.
- When changes take effect and how they are ordered against submitted/applied updates.

Increasing the four-seat clamp alone is not enough: spawning/map capacity, metadata, visibility, UI, turn progression, save compatibility, server behavior, and tests must agree. Review existing code for fixed two/four-side assumptions before enabling a new limit. Do not choose a persistent team format until that feature is approved.

## Compatibility decisions

Existing local saves and PBp matches have not been approved for disposal.

- Keep protocol 4 migration into 5.
- Decide whether missing/blank `appVersion` remains accepted, including current-protocol snapshots. The code currently accepts it after protocol validation.
- The TODO in `TryValidatePbpLoadAppVersion` still refers to removing the bridge when protocol 3 support ends, although protocol 3 is already rejected. Correct the policy and comment in a separately scoped code change.
- Use supported/mismatched/missing-version fixtures and continued play after migration to validate any retirement.
- Keep file/in-memory/null transports until their uses and removal scope are checked.

## Later housekeeping

- `MainMenuUITKView` (4,692 lines) and `MainMenuController` (3,608 lines) are later candidates for bounded pane/refresh/platform responsibility extraction. Preserve serialized fields and UI wiring.
- `AIVsAIBatchRunController` (2,955 lines) is useful tooling; length alone is not a removal reason.
- Tracked `Assets/TempScreenshots/` captures, orphan folder metadata, and the tracked `My project 2.slnx` are housekeeping candidates after reference checks. Phase 1 does not delete them.
- `UIClickInterceptorDebugger` is gated by `UNITY_EDITOR && UI_CLICK_DEBUG` and bootstraps itself at runtime. It still uses legacy mouse input despite the New Input System. Check whether the debug tool is needed before migrating/removing it; missing scene references do not prove it is unused.
- Keep the pre-UITK audit under `Docs/archive/` as history.

## Existing validation evidence

On 2026-10-07, the pre-cleanup code passed server lint/syntax, the adjacent empty enemy city capture PlayMode test, and all 11 EditMode tests with graphics enabled. Isolated sequential relay operations passed; the concurrent-submit case failed its intended semantics.

Ignored `Logs/RestartAudit/` contains local Unity results. These results do not replace complete-match/device or deployed acceptance. Phase 1 validation checks documentation links, factual claims against source, whitespace, and a documentation-only diff.
