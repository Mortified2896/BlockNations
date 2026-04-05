# PbP Migration Ledger

## Versioning Rules

- Bump `appVersion` when mixed-build PbP play should be blocked for safety.
- Bump PbP `protocolVersion` when serialized payload meaning or load semantics change.
- Support at most one explicit migration source at a time unless there is a strong reason to carry more.

## Migration History

| App Version | PbP Protocol | Migrates From | Main Change | Status | Cleanup Note |
| --- | --- | --- | --- | --- | --- |
| Pre-current typed-units step | 3 | None | Warrior/Scout typed-unit payloads with legacy whole-number health; some older saves may omit `appVersion`. | Legacy migration source still accepted. | Remove support when protocol 4 migration window closes. |
| Current build line | 4 | 3 | Persist scaled combat health via `currentHealthUnits`; accept protocol 3 on load; bridge missing `appVersion` for supported legacy saves; rebuild migrated snapshots as protocol 4. | Active. | Drop protocol 3 load support and the missing-`appVersion` bridge together. |

## Update Rule For Future Releases

- Add one row when a PbP-relevant release changes version gating, protocol, or migration support.
- Update the current row when the migration bridge or cleanup note changes.
- When dropping the old migration source, remove or mark the obsolete row clearly so the ledger still reflects the live support window.
