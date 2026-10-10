# PBp Compatibility Policy

Current PBp snapshots use protocol **5**. The current client also accepts protocol **4** as its single migration source. Preserve these supported paths until a release decision explicitly retires them.

Last reviewed: 2026-10-10. Source authority: `PbpAppVersionPolicy` and the protocol/snapshot build/load paths in `Assets/Scripts/Core/TurnManager.cs`.

## Version fields

| Field | Current meaning |
| --- | --- |
| `protocolVersion` | Snapshot meaning/load compatibility: writes 5, accepts 5 or 4 for PBp loads. |
| `appVersion` | Application build version (`1.0.4` in current project settings). The v1.0.4 client also imports v1.0.3 snapshots through the explicit previous-build path. New exports write the current version. |
| `version` | Legacy save-schema string (`"3"` in `GameSave`), separate from PBp protocol. |

The [HTTP contract](HTTP_PBp_Transport_Contract.md) describes request/response shapes. Transport success is not proof that a snapshot will pass client compatibility gates.

## Current load and migration behavior

- Missing/invalid PBp protocol and protocols outside 4/5 are rejected.
- Protocol 4 is normalized through the current load path and rebuilt as protocol 5 on subsequent save/export. Existing legacy two-side ownership/turn/name/visibility fields provide fallback data.
- Protocol 5 carries explicit seats, current turn seat, owner seats, per-seat metadata/gold, explored-seat data, and transport sequence.
- A present nonblank `appVersion` must pass the build policy, including on protocol 4. Version 1.0.4 additionally accepts 1.0.3 as its explicit import source, preserving the previously supported snapshots; earlier/newer unrelated builds are rejected. Protocol acceptance does not bypass app-version gating.
- The v4 combat balance requires build 1.0.4. New exports identify that build; the older 1.0.3 client's existing strict gate rejects them, requiring the other player to update before continuing. Import does not overwrite a preserved source snapshot. The previous-build exception is specific to 1.0.4, not permanent permission for future builds to accept 1.0.3.
- A missing/blank `appVersion` is still accepted by a temporary bridge for an otherwise supported PBp load. The code comment referring to dropping protocol 3 is stale; it does not describe a restriction enforced by this bridge.
- Forward migration does not promise that an older client can read the newly saved game.

## Combat representation

Protocol 4 introduced persisted `currentHealthUnits` using `CombatScale = 10`. Protocol 5 retains this representation. Displayed rule values `1.0`, `0.5`, and `0.1` correspond to stored `10`, `5`, and `1`. Changes to combat scaling or wire/save number meaning require compatibility review.

## Future releases and retirement

- Bump `appVersion` when mixed-build PBp play should be blocked for safety.
- Bump `protocolVersion` when serialized payload meaning or load semantics change.
- Keep at most one explicit migration source unless a broader window is justified and approved.
- Keep a reference build/fixture from the preceding supported protocol for migration checks.
- Existing local saves and PBp matches have not been approved for disposal. Do not remove a compatibility branch because a document or comment calls it old.
- Decide protocol 4 retirement and the missing-app-version bridge explicitly, then update code, [the migration ledger](PbP_Migration_Ledger.md), and release notes together.
- Additional players, team relationships, or membership changes may require protocol work. They are future requirements, not permission to change the current format during cleanup.

## Upgrade validation

Use a targeted checklist for a compatibility change:

- Current client loads a supported old fixture with the required app version and migrates it.
- Migrated data saves as the current protocol and continues after a turn without re-triggering migration.
- Explicit incompatible app versions and unsupported older/newer protocols are rejected with appropriate UI.
- Missing-app-version fixtures follow the explicitly chosen bridge policy.
- Legacy two-side fixtures preserve ownership, turn state, combat health, explored state, and transport progress.
- Current 2-, 3-, and 4-seat games preserve viewer POV, turn ownership, and submitted/applied sequence independently, including eliminated/resigned seats.

Check an old client against a new snapshot for safe rejection or an explicitly supported path; do not assume backward compatibility is required.
