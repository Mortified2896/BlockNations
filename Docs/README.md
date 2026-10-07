# Block Nations Documentation

Start with the current state, then use the relevant implementation/setup reference. Active guidance was reviewed on 2026-10-07 against source revision `0d510b3`. Phase 1 documentation cleanup is complete; AI and hosted playtesting remain separate work.

## Current guidance

| Document | Purpose |
| --- | --- |
| [Repository instructions](../AGENTS.md) | Scope, implementation safeguards, players/teams, UI, validation. |
| [MVP Current State](MVP_Current_State.md) | Implemented modes, UI, AI, PBp, known validation limits. |
| [Roadmap](roadmap.md) | Phase 1, phase 2 AI direction, hosted playtesting, future capacity/teams. |
| [Experimental Hard AI](Hard_AI_Design.md) | Fair local tactical policy, deterministic work budget, inspector, validation, and owner-watched tournament gate. |
| [Development Follow-ups](Development_Followups.md) | Known issues, proposed boundaries, unresolved decisions, targeted checks. |
| [PBp Compatibility Policy](PBp-Compatibility.md) | Current protocol/app-version gates and retirement policy. |
| [PBp Migration Ledger](PbP_Migration_Ledger.md) | Protocol 3/4/5 history and current migration window. |
| [HTTP PBp Transport Contract](HTTP_PBp_Transport_Contract.md) | Active relay endpoints, sequences, errors, persistence limits. |
| [Local Server Setup](Local_Server_Setup.md) | Local authenticated Node relay and client configuration. |
| [VPS Deployment](VPS_Deploy_MVP.md) | Supported Node entry point, deployment, platform credentials, hosting choices. |
| [Unity Testing](Unity_Testing.md) | PlayMode/EditMode commands and graphics/licensing constraints. |
| [Git Workflow](BlockNations_Git_Workflow.md) | Existing local Git identity, SSH, and completion workflow. |

## Historical references

[Historical Planner Workflow](Planner_Workflow.md) preserves the former ChatGPT-to-Codex handoff conventions and model catalogue. It is not active implementation guidance.

[Pass 1 pre-UITK audit](archive/Pass1_Audit_pre_UITK.md) predates the current UI and mode removals. Keep it for history; its file list and refactor suggestions are not current implementation instructions.

The active transport contract moved from `HTTP_PBp_Transport_Contract_v0.md` to `HTTP_PBp_Transport_Contract.md`. The local setup guide moved from extensionless `LocalServerSetup` to `Local_Server_Setup.md`.
