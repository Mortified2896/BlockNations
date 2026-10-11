# Advanced 7×7 economy prototype

Started 2026-10-11. This experimental 7×7 mode has a standalone **single-player browser playtest** against a deterministic scripted opponent, plus a two-human Editor window. It has no AI training or tutorial integration. Matches exist only in memory; refreshing, closing or starting a new match resets them.

## Playtest

From the normal main menu, choose **Advanced 7×7 (experimental)** below **Play vs AI**, or use the browser footer's **Advanced 7×7** link beside **Account**. You can also open **https://blocknations.moneymattersmedia.com/advanced/** using your approved Google account. The browser opens the playtest in the same tab and keeps your existing sign-in. You control the blue seat; the red scripted opponent acts after **End turn**. Pick a balance preset before pressing **New match**.

For local hotseat play, use Unity **6000.4.0f1** and open **Tools → Block Nations → Advanced 7x7 Playtest**. Play Mode and a scene are not required.

- Choose male or female population and recruit a Warrior, Rider or Archer using the city controls. Move the stationed unit before recruiting again.
- Select one of your units on the 7×7 board, then click a green destination to move or attack. The shared simulation handles movement, combat, visibility, hidden blockers and action budgets. Starting city positions are public; enemy units outside current vision remain hidden.
- Upgrade Housing, Food or Gold using the city buttons. Read current output and the recruitment forecast before buying. An upgrade raises capacity; remaining civilian workers also limit actual output.
- In the browser, end the turn and wait for the scripted opponent to finish. In the Editor hotseat window, pass control; the next player's board stays behind a handoff screen until they reveal it.
- Capture the opponent's city to win. **New match** resets the experiment. Disband an unused soldier at its home city to return its population point; moving or attacking prevents disbanding that turn.

The browser uses its own touch-oriented UITK view and a scene generated only inside a dedicated build snapshot. The normal main menu links to this separate browser playtest. Persistence and browser multiplayer remain separate integration work.

## Rules and provisional values

Population points represent recruitable adult cohorts. Births abstract gradual replenishment, rather than modelling individual children or maturation. A point is not necessarily a single person. Men and women have identical food needs and worker output.

| Resource or action | Prototype rule |
| --- | --- |
| Starting city | 6 male + 6 female civilians; housing for 16 residents; 12 stored food. |
| Opening gold | 6 before income; second player gets +1. Each receives city income at its own first turn: 9 / 10 gold. |
| Recruitment | Existing 2-gold unit cost + 1 civilian point of the chosen sex; once per city per owner turn; empty city tile required. |
| Housing | Counts civilians **and surviving mobilized soldiers** from that city; recruitment/disbanding cannot create capacity. |
| Food production | Lesser of farm capacity (16 + 4 per upgrade) and 2 × civilian population. |
| Food consumption | 1 food per civilian or soldier per owner turn. City storage is capped at 24 after consumption. |
| Growth | Each owner turn adds `min(civilian men, civilian women)` progress, only with spare housing and positive food production balance. At 12 progress, create at most one population point and spend 2 stored food. Birth sexes alternate deterministically. |
| Growth pause | No pairs, full housing or no surplus pauses progress. It does not accumulate additional progress while paused. |
| Starvation | Production plus stored food first cover consumption. Any shortfall resets growth and loses at most 1 civilian point from the larger sex pool; tie losses balance over time. Soldiers without rations also lose 1 HP, with civilians receiving ration priority and newer soldiers losing rations first. |
| Gold production | Lesser of market capacity (3 + 1 per upgrade) and `ceil(civilians / 4)`. Recruitment can lower productivity. |
| Unit upkeep | 1 gold per living soldier per owner turn. Income plus treasury pay older units first; each unpaid unit loses 1 HP. Food and gold shortages can each damage the same unit. No negative treasury. |
| City upgrades | Housing +4 capacity, Food +4 capacity, Gold +1 capacity. Cost 4, then 8, then 12 gold; maximum 3 levels each; at most 1 upgrade per city per turn. Recruitment and an upgrade may both happen. |
| Disbanding | Returns 1 point of the soldier's recorded sex at its home city, with no gold refund. Requires unused movement/attack budgets and blocks recruitment at that city for the rest of the turn. Housing already includes the soldier. |
| Combat deaths | Remove the soldier permanently from its home city's residents and from future food/gold upkeep. Casualties do not return population. |

Growth resolves at the start of the incoming seat's turn, after the standard kernel resets actions. Standard kernel city income is zero in this wrapper; the advanced economy is the sole income source. Food/production use the pre-birth population. Food casualties and hunger resolve before gold upkeep; survivors pay upkeep. Each city has its own food and population pool; gold belongs to the seat.

Recruiting a man lowers civilian pairs immediately if men become the smaller group. Losing that soldier makes the loss permanent; a survivor can restore the pool by returning and disbanding. Recruiting women has the corresponding effect. A sex pool can reach zero, stopping growth. The UI warns when a recruitment would do that; it does not silently forbid the choice.

## Combat balance choice

Both presets preserve current unit health, range, vision, movement and attack budgets:

- **Equal** (provisional default): male/female recruitment changes population composition and future growth, with equal combat strength.
- **ClassStrengthExperiment**: female Warrior attack 0.8 versus 1.0; female Archer/Rider attack 0.9 versus 1.0. Official registry definitions remain unchanged, and each recruited unit retains its own definition. These numbers are experimental game balance, not evidence about real-world trained combatants. Female units cost the same and have the same upkeep in this preset, so dominance by male recruitment is a specific playtest risk.

The intended product default remains a design choice. Both exist so the choice can be tested without changing the basic population model. One recruit uses the current preset and the explicit sex choice, rather than multiplying an existing unit repeatedly.

## Architecture and compatibility boundary

- `AdvancedEconomyRules`: a distinct versioned prototype recipe and optional combat preset.
- `AdvancedEconomyState`: city cohorts, infrastructure, soldier origins, forecasts and actual settlement reports.
- `AdvancedMatch`: explicit seat ownership, advanced purchases and disbanding, shared combat/movement transitions, and per-owner-turn economy settlement.
- `AdvancedEconomyPlaytestWindow`: two-human in-memory board and city controls. Commands enter the wrapper; standard recruit commands are rejected because they omit the population choice.
- `AdvancedSimpleOpponent`: explicit-seat legal purchases, movement and attacks; uses its own economy and visible legal actions. It reserves food/upkeep, preserves civilian pairs and targets the public opposing city. It does not load learned actors or inspect hidden enemy units.
- `AdvancedGamePlaytestView`: browser UITK controls, visible board, purchase forecasts and animated opponent turns. Serialized references are wired by the dedicated build script.
- `AdvancedPlaytestUiTests`: graphics-backed regression coverage for legal purchase buttons and turn restrictions.
- `AdvancedEconomyTests` and `AdvancedSimpleOpponentTests`: rule invariants, standard-economy regression coverage, fog invariance and bounded complete scripted matches, executable in Unity and a standalone .NET test project.

The existing shared `MatchEngine`, `MatchState`, unit catalog, learned schema, training workers, product `GameMode`, gameplay serialization and PBp protocols are unchanged. The mutable `Board` property exposes the existing kernel state to the Editor renderer and tests; any future adapter must send commands through `AdvancedMatch`, rather than call `MatchEngine.Apply` or mutate that board directly.

Persistent saves or browser multiplayer need an explicitly approved versioned advanced payload, with city demographics/infrastructure/growth/food, soldier sex/home city and rules/preset identity. An advanced snapshot must never load as a standard economy match or acquire silently different rules during fast iteration. Preserve supported standard saves and PBp migrations.

## Design questions for the next iteration

1. **Combat tradeoff:** should sex affect combat at all? Pairing and worker loss already create choices. If female units are weaker at identical costs, test whether anyone rationally recruits them outside extreme demographic imbalance. Specialization or a cost/upkeep distinction might create a choice, but needs explicit agreement.
2. **Time scale:** abstract replenishment keeps games short. Children, maturation, age and pregnancies would need a longer match pace and more state; defer them unless they become a core goal.
3. **Recovery:** disbanding helps, but losing an entire civilian sex with no surviving soldier is unrecoverable in this prototype. Decide on migration/refugees or minimum surviving cohorts before calling the rules balanced.
4. **Failure pace:** reserves and one-civilian-loss-per-turn give warning time; simultaneous food and upkeep damage can still cause an army collapse. Decide whether troops should desert or lose effectiveness instead of HP.
5. **Spatial economy:** city upgrades currently occupy no board tiles. Districts or nearby farms could compete with maneuver space on 49 tiles; start with city upgrades until there is a reason to add worker placement.
6. **Match length and victory:** the shared baseline ends on the first city capture. Population growth may be too slow to matter in short rush games. Measure human turn counts and upgrade payback before adding economic victory, rebuilding or multi-city capture.
7. **Snowball and second-player fairness:** population compounds; gold upgrades compete with recruiting now. Check both starters, aggressive recruitment, balanced recruitment and development openings in human playtests, without spending time on AI training.
8. **Player clarity:** normal mobile UI should show men/women, soldiers, housing, food reserves/net food, growth progress, gold income/upkeep and exact post-recruit consequences. Show starvation/unpaid warnings before a purchase.

## Validation

Run the shared rules without Unity:

```sh
dotnet test Tools/AdvancedMode/BlockNations.AdvancedMode.Tests.csproj
```

Run Unity EditMode tests using the installed Editor and `-testFilter "AdvancedEconomyTests|AdvancedSimpleOpponentTests"` (see [testing notes](Unity_Testing.md)). The pure economy fixture supports `-nographics`; the complete Editor suite needs graphics. Check a fresh passed result, not just a process exit.

Build the dedicated browser export with `python3 Tools/AdvancedMode/build.py`. It copies only shared simulation, advanced view/layout/template, three unit sprites and project settings into an owned snapshot outside the checkout. It excludes learned actors, saves and account credentials. Unity generates its build scene inside that snapshot; no existing scene is edited. `Build/AdvancedWeb/release.json` identifies the rules and source hash. The release audit checks exported content before deployment.

Deploy with `npm run deploy:advanced --prefix Web` after setting the existing session signing secret privately on `blocknations-advanced`. The Worker serves only `/advanced/` on the existing hostname and reuses the existing approval service and host-only session. Its scoped route and assets are independent of the normal game, tutorial and multiplayer releases. Anonymous entries redirect to `/access`; anonymous binary assets return 403. If signing in takes you back to the normal game, reopen the advanced link.

The browser publishes `window.advancedGameState` for read-only QA, containing only the local economy and visible units. Browser checks must use actual UI actions and confirm the resulting state. Automated tests establish rule invariants and bounded opponent behavior, not gameplay balance or physical iPhone acceptance.
