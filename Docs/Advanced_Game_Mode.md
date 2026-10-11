# Advanced 7×7 mode

Started 2026-10-11. This experimental 7×7 mode launches inside the normal game and uses its existing board, sprites, fog, action markers and touch input. It runs against a deterministic scripted opponent; a separate two-human Editor window remains available for rule experiments. It has no AI training or tutorial integration. Matches exist only in memory; refreshing, closing or starting a new match resets them.

## Playtest

Open **https://blocknations.moneymattersmedia.com/** with your approved Google account and choose **Advanced 7×7** directly below **Play vs AI**. The button loads the dedicated Advanced scene inside the same Unity game. Legacy `/advanced/` links redirect to this main menu.

- Tap your blue city, or the **City** toolbar button. **Recruit** offers male/female population choices and Warrior, Rider and Archer cards with their cost and attack/range.
- **Develop** offers Housing, Farm and Market upgrades. Each card displays its level, cost and actual immediate output gain, including a zero gain when workers limit production.
- **Population** shows civilian men/women, soldiers, growth pairs/progress and production/consumption. The resource bar stays on the board and shows gold/net income, civilian population, occupied housing and stored/net food.
- Select your unit on the original game board and tap a destination or target. Movement, attacks, fog and yellow action markers use the same presentation/input components as ordinary gameplay. The unit sheet displays sex, combat stats and upkeep. Disband an unused unit at home to restore its population point.
- Press **End turn** to run the scripted opponent. Capture its city to win; **Play again** starts a fresh advanced match.
- **Menu** offers return to the game, the main menu, and an explicit new match with either equal strength or the class strength experiment. Choosing a preset affects the new match only.

For local hotseat rule experiments, use Unity **6000.4.0f1**, **Tools → Block Nations → Advanced 7x7 Playtest**. Play Mode and a scene are not required for that development window.

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

Recruiting a man lowers civilian pairs immediately if men become the smaller group. Losing that soldier makes the loss permanent; a survivor can restore the pool by returning and disbanding. Recruiting women has the corresponding effect. A sex pool can reach zero, stopping growth. The UI shows the number of remaining growth pairs before recruitment; it does not silently forbid a zero-pair choice.

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
- `AdvancedMatchController`: the temporary native match driver and stable simulation-to-scene ID mapping. It projects wrapper results into the established units/cities, preserves per-unit combat definitions, and runs the scripted opponent incrementally.
- `AdvancedGameplayUITKView`: an explicitly wired UITK resource bar, city/unit sheets and menu; the original scene renderer/input continues to own the board.
- `SceneSimulationAdapter` and `LegalActionService`: use the authoritative advanced state and its stable IDs only in the dedicated scene; ordinary matches retain their existing capture/project boundary.
- `AdvancedNativeModeTests`: the actual main-menu launch, native recruitment/movement, population deduction, exact income/upkeep settlement, opponent turn, capture, restart, combat preset and save/load isolation.
- `AdvancedEconomyTests` and `AdvancedSimpleOpponentTests`: rule invariants, standard-economy regression coverage, fog invariance and bounded complete scripted matches, executable in Unity and a standalone .NET test project.

The shared `MatchEngine`, `MatchState`, unit catalog, learned schema, training workers, product `GameMode` values, gameplay serialization and PBp protocols remain compatible. The external match boundary identifies advanced mode explicitly without adding a persisted enum or payload. The mutable `Board` property exposes the existing kernel state to presentation and tests; all gameplay commands enter `AdvancedMatch`. Standard kernel income and begin-turn helpers reject an active advanced match, preventing double settlement. The scene disables autosaving, and advanced save/load entry points cannot overwrite or import a standard match.

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

Build the normal account game with `python3 Tools/WebRelease/build.py --multiplayer --learned-ai`, using an owned snapshot outside the checkout. `SinglePlayerWebBuild` includes MainMenu, SampleScene, Tutorial and Advanced. The frozen ordinary opponent remains separate from the advanced scripted opponent. `Build/WebRelease/release.json` records the source hash and the included advanced scene.

Audit and deploy the normal bundle with `npm run deploy:production --prefix Web`. Deploy the legacy redirect with `npm run deploy:advanced --prefix Web`; that Worker has no game asset binding and serves no independent prototype bytes. Google admission, standard saves and PBp transport continue through their established paths.

For acceptance, inspect the actual main-menu click into the Advanced scene, native board movement/recruitment, resource changes and the opponent's return turn. Check the resource bar and all city tabs on narrow portrait and desktop viewports. A successful deployment alone does not prove a signed-in physical iPhone session.
