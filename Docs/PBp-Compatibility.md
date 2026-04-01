# PBp Compatibility Policy

This note records the current Play-by-Post compatibility/versioning policy for the repo.

## Current Protocol Version

- Current PBp game/snapshot protocol baseline: `3`
- Legacy migration source still accepted during rollout: `2`
- Separate transport-contract reference: `Docs/HTTP_PBp_Transport_Contract_v0.md`

Game/snapshot protocol governs save/load and cross-client compatibility.
The HTTP transport contract doc describes request/response shape only.

## Protocol History / Upgrade Notes

- `3`: current game/snapshot protocol baseline.
- `2`: temporary migration source retained for legacy save compatibility.
- Any future PBp game/snapshot upgrade must be called out in code, commit messages, and changelog notes.

## Migration Policy

- Prefer forward-only migration for supported PBp content and snapshot formats.
- Newer typed-unit games do not need to remain backward compatible with older protocols.
- If a game is saved or migrated into a newer PBp format, the newer format is the only supported path for continued play.

## Temporary Migration Support

- Legacy migration rules may exist only for the active rollout window.
- Remove temporary compatibility paths after rollout completes.
- Do not leave old migration branches in place once the new protocol is stable.

## Old Build Retention

- Keep the last build from the previous PBp protocol era for regression testing and recovery checks.
- Treat that build as the reference client for compatibility verification during upgrades.

## Compatibility Test Checklist

Use this checklist for future PBp upgrades:

- Old client opens new game.
- New client opens old game.
- Migrated game continues after one turn.
- Local snapshot does not re-trigger the legacy path.
- Block/upgrade UX is clear to the player.

## Change Notes

- Future protocol or content upgrades should be explicitly mentioned in commit messages and changelog entries.
