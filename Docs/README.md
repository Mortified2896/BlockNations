# Block Nations Documentation

Start with the current state, then use the relevant implementation/setup reference. Active guidance was reviewed on 2026-10-07 against source revision `0d510b3`. Phase 1 documentation cleanup is complete; AI and hosted playtesting remain separate work.

## Current guidance

| Document | Purpose |
| --- | --- |
| [Repository instructions](../AGENTS.md) | Scope, implementation safeguards, players/teams, UI, validation. |
| [MVP Current State](MVP_Current_State.md) | Implemented modes, UI, AI, PBp, known validation limits. |
| [Roadmap](roadmap.md) | Phase 1, phase 2 AI direction, hosted playtesting, future capacity/teams. |
| [Experimental Hard AI](Hard_AI_Design.md) | Fair local tactical policy, generic evaluator/replies, deterministic work, decision records, inspector, and watched comparisons. |
| [Local ML Training MVP](ML_Training_MVP.md) | Mac self-play controls, storage/sleep limits, saved/resumed checkpoints, local model playtesting, schema and validation limits. |
| [General AI Training Direction](AI_Training_Direction.md) | Proposed shared simulation, adaptable policy representation, fair memory, reproducible experiments, and local phone inference. |
| [Shared C# Simulation](Shared_Simulation_Implementation.md) | Implemented rules kernel, gameplay/training adapters, compatibility corrections, parity checks, measured throughput and remaining boundaries. |
| [Decoupled Training and Viewer](Decoupled_Training_Implementation.md) | Shared C# parallel arenas, one PPO learner, independent replay/live viewer, resume compatibility and measured Mac throughput. |
| [Development Follow-ups](Development_Followups.md) | Known issues, proposed boundaries, unresolved decisions, targeted checks. |
| [PBp Compatibility Policy](PBp-Compatibility.md) | Current protocol/app-version gates and retirement policy. |
| [PBp Migration Ledger](PbP_Migration_Ledger.md) | Protocol 3/4/5 history and current migration window. |
| [HTTP PBp Transport Contract](HTTP_PBp_Transport_Contract.md) | Active relay endpoints, sequences, errors, persistence limits. |
| [Local Server Setup](Local_Server_Setup.md) | Local authenticated Node relay and client configuration. |
| [VPS Deployment](VPS_Deploy_MVP.md) | Supported Node entry point, deployment, platform credentials, hosting choices. |
| [Single-player Web Release](SinglePlayer_Web_Release.md) | Isolated credential-free browser build, local AI, Cloudflare release targets and validation. |
| [Learned AI Browser Playtest](Learned_AI_Browser_Playtest.md) | Frozen 7×7 local policy, experimental difficulty/variety presets, fair observations and release validation. |
| [Unity Testing](Unity_Testing.md) | PlayMode/EditMode commands and graphics/licensing constraints. |
| [Git Workflow](BlockNations_Git_Workflow.md) | Existing local Git identity, SSH, and completion workflow. |

## Historical references

[Historical Planner Workflow](Planner_Workflow.md) preserves the former ChatGPT-to-Codex handoff conventions and model catalogue. It is not active implementation guidance.

[Pass 1 pre-UITK audit](archive/Pass1_Audit_pre_UITK.md) predates the current UI and mode removals. Keep it for history; its file list and refactor suggestions are not current implementation instructions.

The active transport contract moved from `HTTP_PBp_Transport_Contract_v0.md` to `HTTP_PBp_Transport_Contract.md`. The local setup guide moved from extensionless `LocalServerSetup` to `Local_Server_Setup.md`.
