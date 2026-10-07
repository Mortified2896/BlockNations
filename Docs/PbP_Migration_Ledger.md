# PBp Migration Ledger

The current snapshot protocol is **5**, with **4** as the supported migration source. This ledger records representation changes separately from app-version gating. See [compatibility policy](PBp-Compatibility.md) for the current load rules.

Last reviewed: 2026-10-07, against source revision `0d510b3`.

## Migration history

| Protocol | Representation | Current status | Follow-up |
| --- | --- | --- | --- |
| 3 | Typed Warrior/Scout units with legacy whole-number health and two-side fields; some saves omit `appVersion`. | Historical; rejected by the current PBp protocol gate. | Keep only as historical context or explicit fixtures. |
| 4 | Scaled combat health persisted as `currentHealthUnits`; legacy two-side snapshot representation. | Single supported migration source into 5, subject to app-version gating. | Retire only after an explicit decision about existing saves/matches. |
| 5 | Explicit seat count/current turn seat/transport sequence, seat metadata and gold, unit/city owner seats, and explored-seat data, alongside legacy bridges. | Current write/load protocol; PBp currently supports 2–4 seats. | Future capacity/team/membership changes need a separately approved design and compatibility review. |

The current project app version is `1.0.3`; this does not date the introduction of every historical protocol. A present `appVersion` must match the client even for a supported migration source. Missing/blank `appVersion` still passes the temporary bridge. The source TODO tying that bridge to protocol 3 retirement is stale and awaits a policy decision.

## Versioning and update rules

- Bump `appVersion` when mixed-build PBp play should be blocked.
- Bump `protocolVersion` when serialized meaning or load semantics change.
- Support at most one explicit migration source unless a broader window is justified and approved.
- Add a row for a new representation and update existing status/retirement notes when the support window changes.
- Keep the compatibility policy, factual current state, code gates, and release notes consistent.
- Retirement of old data and the missing-app-version bridge is unresolved; phase 1 documentation cleanup changes neither.
