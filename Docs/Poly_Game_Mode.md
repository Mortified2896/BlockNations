# Poly land playtest

Poly is an owner-only Vs AI mode in the Block Nations website. Choose **Poly 11×11** from the main menu. It continues your separate Poly match automatically; **New match** asks before replacing that continuation slot. Unity Editor access is also available. Website launch permission is returned by the verified account session only when the approved administrator matches `POLY_OWNER_USER_ID`.

## Playing

- Start on an 11×11 seeded land map with one city and a Warrior. The opponent has the same starting position resources, with one extra opening Gold when its first turn begins. Five neutral villages provide expansion through capture.
- Tap your unit, then a yellow reachable tile or red attack target. Drag the map and pinch to zoom using the normal Block Nations camera. On an occupied city, the first tap shows the unit; repeated taps alternate unit → city → unit. Occupied cities open compact stats instead of unavailable recruitment cards; a Unit tab also returns to the stationed unit. Landscape inspection sits beside the board. Tap an empty city to recruit a Builder or combat unit. Move a Builder onto land and choose **Build here** to develop it. **Tech** opens an illustrated tree with round nodes, dependency arrows and selectable branches. Recruitment and city development use page buttons; there are no map or gameplay-panel scrollbars.
- Spend Gold on technology, recruitment and permanent tile improvements. Food from improvements grows city population every owner turn. Population is not consumed by recruitment.
- City territory remains 3×3 and never expands with levels. Construction is independent of territory: Builders can improve any land they can reach, including unclaimed or enemy city territory, with the required technology. City centers cannot be developed.
- City levels require 2, then 3, then 4, and successively more population. Surplus carries forward. Each level adds one Gold per owner turn and queues a reward choice.
- Level 2 offers Workshop (+1 Gold/turn) or Fortifications (city defense). Level 3 offers 5 immediate Gold or 2 population. Level 4 offers another Workshop or Fortifications. Level 5 onward offers a Giant or 10 Gold. A Giant appears on an empty city/territory tile; if there is no room, the economic reward remains available.
- Cities support `level + 2` living units recruited there. Recruitment needs an empty city and is limited to once per city per owner turn. Recruits and reward Giants become ready on the following owner turn.
- Enter a village or enemy city, then capture it on your following turn while standing there with unused actions. Taking the opponent's last city wins. Other captures continue the match.
- Rest an unused unit to heal 4 HP in friendly territory or 2 HP elsewhere. Surviving defenders retaliate when the attacker is in their range. Catapults cannot retaliate or fire after moving. Riders may retreat after attacking; Knights can attack again after a kill.

The unit roster is Warrior, Archer, Rider, Defender, Swordsman, Knight, Catapult, Giant and Builder. Builders cost 3 Gold, need no technology, move 2, have 10 HP and defense 1, and cannot attack, retaliate or capture cities. They are reusable. A Builder must occupy the construction tile; it can build after moving, and construction ends its turn. Balance values are provisional Poly values. There is no water, Mind Bender, Cloak, science resource or AI training in this rules version.

## Economy and technology

Research costs `tier × (4 + owned cities)` Gold and requires its parent. The illustrated technology tree shows prices, unlocks and prerequisites, including unaffordable nodes. Selecting a locked tile also offers a research-only button for its next prerequisite and a combined research-and-build purchase. The latter includes all missing parents, itemizes research and building costs, and spends nothing unless the complete action is legal and affordable.

| Branch | Unlocks |
| --- | --- |
| Organization → Farming → Construction | Orchards; farms; adjacency windmills |
| Organization → Shields | Defenders |
| Hunting → Archery | Hunting huts; Archers and forest defense |
| Hunting → Forestry → Mathematics | Lumber huts; Catapults and adjacency sawmills |
| Climbing → Mining → Smithing | Mountain movement/defense; mines; Swordsmen and adjacency forges |
| Riding → Roads → Chivalry | Riders; roads; Knights |

Improvements retain their resources and terrain. Improvements belong to their builder's player and connect automatically to that player's nearest city, with equal distances resolved by city ID. Gold connection lines on the board emphasize the selected tile or city. Tile inspection shows the receiving city, tile distance and efficiency. Enemy occupation blocks production; a unit can capture an enemy improvement on its following turn. City capture reroutes each player's improvements to their nearest remaining city. Green grain badges and Gold coin badges show numeric effective yields on developed tiles; the city badge shows its total Gold income. Inspection shows current yields, improvement gains and prices. A city's growth meter shows stored Food and turns until its next population point.

| Improvement | Build cost | Base yield per owner turn |
| --- | ---: | --- |
| Orchard on fruit | 2 Gold | 1 Food |
| Hunting hut on animals | 2 Gold | 2 Food |
| Farm on crops | 5 Gold | 3 Food |
| Lumber hut in forest | 3 Gold | 1 Gold |
| Mine on ore | 5 Gold | 2 Gold |
| Windmill | 5 Gold | 2 Food per adjacent farm |
| Sawmill | 5 Gold | 1 Gold per adjacent lumber hut |
| Forge | 5 Gold | 2 Gold per adjacent mine |

A tile supports one improvement plus a road. A city supports one of each windmill, sawmill and forge; these can use an unimproved plains tile when their matching neighbours exist. Adjacency includes the eight surrounding cells with the same improvement owner, independently of city territory. Building another matching improvement immediately increases the hub's yield; the tile purchase preview includes that gain.

Yield is divided by the distance to the receiving city: adjacent 100%, distance 2 50%, distance 3 one-third, distance 4 25%, continuing down to distance 10 10%. Distance counts tile steps, including diagonal steps. Fractional Food and Gold accumulate exactly across turns and saves; whole Gold becomes spendable when the accumulated remainder reaches one. At the start of each owner turn, Food is collected once. Each `4 + 2 × city level` Food produces one population point, with surplus retained. Population thresholds for levels remain 2, 3, 4 and so on; level rewards remain unchanged. Gold income is `city level + 1 + workshops + tile Gold yields`, collected once after growth. Opening Gold remains 7 for the human and 8 for the opponent including its first income. Roads cost 2 Gold, have no economic yield, and remove rough-terrain movement costs. Mountains require Climbing; forests/mountains cost two movement points, with an adjacent passable tile always reachable using a unit's one movement action.

## Fog and heuristic opponent

Poly uses `MatchEngine.Visibility` from ordinary gameplay. Visibility is live, can shrink after a spotter leaves, and is separate from remembered terrain. Archers retain the normal two-tile vision; other Poly units and cities see one tile. Unseen units are hidden. The map remembers only improvements and city metadata actually observed, rather than displaying current hidden state.

`PolyOpponent` receives a seat observation containing its own resources/cities, its own and currently visible units, remembered terrain, and legal actions. It recruits Builders, moves them onto known reachable resources and constructs improvements. It also grows cities, buys useful technology, recruits combat units, explores, heals, defends and captures through deterministic scoring. The policy receives neither the full match nor the human treasury or hidden pieces. It runs locally and resumes an interrupted saved AI turn without applying income again.

## Save and compatibility boundary

`poly-land-v3` adds improvement/road ownership and exact fractional Food/Gold carry. The loader supports `poly-land-v2` without advancing a turn: existing improvements inherit their current city owner and retain their adjacent full yields, stored Food, treasury, units and action budgets. It also supports `poly-land-v1`: levels, population progress, treasury, turn budgets and fog remain intact; old harvested fruit/animals become orchards/hunting huts, and existing farms/mines/lumber huts gain recurring yields. Migration uses the original map seed and only converts already remembered tiles in fog memory. Loading does not settle Food or income, and the old checkpoint is preserved until the next action saves it. `save_poly_v1_<account scope>.json` is distinct from standard single-player saves and PBp snapshots. It contains the map, city progression, resources, researched technology, live unit/action budgets, per-seat fog memory, current turn and outcome. The codec validates before writing; unsupported/corrupt saves remain available for inspection. The previous checkpoint is retained separately. Browser writes explicitly flush IndexedDB before the saved indicator confirms success or Save & Menu leaves.

Continuation is local to the same browser/device and account. It is not a cloud save or synchronization between devices.

The Poly scene explicitly wires the normal GridManager, CameraController, TileHoverManager, UnitSelectionManager, top HUD, city/unit state managers, tile/city/unit prefabs and New Input System event module. Poly projects its authoritative state into native sprites, world coordinates and fog overlays. The standard Warrior, Rider and Archer sprites are reused; additional units and terrain have small pixel graphics rendered on the same white tiles. PolyGameplayUITKView adds compact bottom sheets and a paged roster, with branch-based research in a modal. It contains no ScrollView or UI grid. LegalActionService and SceneSimulationAdapter dispatch only explicitly wired Poly scenes to the Poly rules. The standard unit registry, learned policy catalog, normal save format and PBp protocol remain compatible. Existing poly-land-v1 and poly-land-v2 checkpoints keep the same account-scoped filename and migrate into the new economy without advancing a turn.

## Validation

Run `dotnet test Tools/Poly/BlockNations.Poly.Tests.csproj` for rules, fog, transitions, heuristic visibility and save-state checks. Run Unity EditMode fixtures `PolyRulesTests|PolyNativeTests|PolyNativePresentationTests` with graphics for the actual Unity JSON codec, account-scoped save isolation, explicit scene/input/UI wiring and build registration. Run `npm test --prefix Web` for identity-gated session permission and existing web regressions.

The ordinary release builder includes Poly alongside existing scenes and records it in `release.json`. Prepare/build an owned isolated snapshot with `Tools/WebRelease/build.py --multiplayer --learned-ai`; audit the export, check browser gameplay and reload/continue, then deploy the same reviewed bytes. Physical phone behavior is a separate acceptance check.

## Initial release evidence: 2026-10-13

The initial interface used a separate UITK board and scrollable sheets. The owner rejected that presentation on iPhone because it did not match Block Nations. The following checks established functionality, not design acceptance; the native presentation correction supersedes that interface.

- Feature commit: `1d92721`, merged and pushed to `main`. Export source hash: `f0a732911761c2ffaacefb1e14799d4d41e98b1d5f3b4917a31a0d05faca8f53`.
- Initial production Worker version: `bffa6836-a350-4a27-b2dc-21ec0b788a55`. Preview version: `00d41eda-8b3f-4a41-9111-2a3a13d76baa`. Both use the same reviewed export.
- All 15 Unity Poly checks, 12 standalone Poly checks, 43 existing Advanced rules checks, 71 web checks and 17 release-tool checks passed. The 31.17 MiB export passed the credential audit; the staged patch and decompressed release also contained zero matches against two local authentication values.
- Chromium acceptance used synthetic accounts with the actual admission Worker, session API and exported Unity game. The owner menu launched Poly; the tester session received `canPlayPoly: false` and its rendered menu hid the entry. Desktop and 390×844 portrait checks covered the board, city sheet, scrollable unit cards and map focus after resizing.
- Actual canvas clicks moved a Warrior, bought Organization, harvested two resources, reached city level 2, selected Workshop, recruited a second Warrior and completed two opponent turns. After Save & Menu and a full page reload, continuation restored round 3, 2 Gold, income 4, city level 2 and the same two units/positions.
- Live production settings matched the configured owner identity and preserved every existing service, namespace, secret binding and origin variable. The active Worker contains the owner-gated session code. Anonymous production release/session requests remained 403 with `private, no-store`.
- A real signed-in owner session on production and physical iPhone/Android or Safari gameplay remain unverified. Browser acceptance above is local Chromium with synthetic identity, separate from the successful hosted deployment.


## Native presentation correction: 2026-10-13

The owner requested the standard Block Nations presentation and removal of mobile game scrollbars. Poly now uses the existing world board, normal unit/city sprites, blue background, fog, camera and touch input. Recruitment and development have paged bottom panels; technology has branch cards and adapts to landscape without scrolling. Modal panels hide the normal header while open. The opening camera fits the 11×11 board above the controls, and drag/pinch remain available.

- Feature commit: `ed67348`; merged into `main` as `cd93adc`, retaining the concurrent multiplayer match-list improvements.
- Export source hash: `884a9bcb7170300d93eff6f1d63a706a470b1da3ab6a919bcb74bdd73b8cd374`.
- Production Worker: `0bd70509-2171-4fa1-bfe0-2b07ec156718`, verified at 100% traffic. Preview Worker: `90eef250-7ae1-4489-84a3-2aa6a9e9e884`. Both use the reviewed Unity export.
- All 66 focused Unity cases passed, including actual native scene movement, recruitment, income/AI handoff, the unchanged existing Poly checkpoint, fog/legality projection, additional unit sprite sizing and standard-save isolation. The standalone 12 Poly rules checks, 17 build-tool checks and final 77 web tests passed. The 31.16 MiB export passed its credential audit.
- Chromium acceptance used the real exported game and admission/session code with synthetic identities. A checkpoint created in the old scrolling interface reopened in the new interface at round 2, Gold 2, population 1 and the same Warrior position.
- Simulated touch taps recruited a Warrior, harvested a resource, chose the level-2 Workshop reward and completed two AI turns. Save & Menu plus a full reload restored round 4, Gold 4, income 4, city level 2 and the same two units at positions 13 and 12.
- Native camera drag changed its position; two-finger touch events changed orthographic size from 12.9875 to 10.5275 while Gold and unit positions stayed fixed. Screenshots cover 390×844 portrait, the research/recruitment panels, and the two-column research layout at 844×390. The earlier 375×667 check exposed the header overlap corrected in this release.
- Live production inspection verified the deployed version, the owner-gated session code, and preservation of every existing owner, origin, service, secret and Durable Object binding. Anonymous release/session requests remain protected.
- A real owner session on the hosted release and a physical iPhone/Safari run of this correction remain separate checks. Browser gameplay evidence above is Chromium with simulated touch, not a physical-device claim.


## Permanent tile economy and inspection: 2026-10-14

The owner requested alternating unit/city inspection, readable tile yields, permanent food improvements, illustrated research dependencies and research purchases from tile inspection. Repeated taps on a stationed friendly unit now alternate unit, city and unit; occupied cities open compact stats so the next tap stays accessible. Landscape inspection uses a side panel. The tree uses the native pixel sprites in round nodes with prerequisite arrows. Tile inspection supports either buying the next prerequisite or atomically purchasing the full missing research chain plus the improvement, with separate research/build costs.

- Feature commits: `373095c`, `de2b42d` and `360ad2b`; merged with the current multiplayer release and unrelated training documentation/tool updates, pushed to `main` as `8a0c031`.
- Export source hash: `b05e901fe2f8fe1f9ab10ecc8278dfec70b2236db35a3bbb5fa08d3f9c3d693e`. The 31.14 MiB export passed its credential audit. Production Worker: `6c74b578-d94c-420f-befd-86ba7678318e`; preview Worker: `0e20e2d8-ffaf-4c36-acd3-95432d8f0f5f`. Both use the reviewed export at 100% traffic; all existing non-asset bindings are preserved.
- All 71 final focused Unity cases passed, plus 17 standalone Poly checks, 85 web checks and 17 release-tool checks. Tests cover recurring Food/Gold, prerequisite pricing, atomic rejection of unaffordable/stale purchases, old-save migration, real scene projection and normal-save isolation.
- Chromium acceptance used the exported Unity game, real account/session code and synthetic identity. Simulated touch passed unit → city → unit at 390×844, 375×667 and 844×390. Screenshots confirm the compact city panel, illustrated branches, visible dependency arrows/costs, tile purchase bills and numeric yield badges. Native drag and two-finger pinch changed the camera without changing Gold, Food or unit position.
- A checkpoint made with the previous release reopened at round 2, Gold 2, population progress 1 and the same Warrior position. Its harvested fruit became an orchard producing 1 Food/turn; loading did not settle income or growth.
- Actual UI clicks bought Hunting plus a hunting hut for 7 Gold (5 research + 2 build), preserved Animals, accumulated Food, reached city level 2 and selected Workshop. Climbing, Mining and a mine cost 20 Gold (15 research + 5 build); Ore remained and income rose to 6 Gold/turn. Research-only purchase of Riding stayed in the tile panel. Save & Menu, full page reload and continuation restored round 11, Gold 1, income 6, city level 2, population progress 1, Food 7/8 and the Warrior at position 12.
- Anonymous live preview/production checks still return 403 for release/session payloads while the public manifest returns 200. Real authenticated owner gameplay on the hosted release and a physical iPhone/Safari run remain separate from synthetic Chromium acceptance.


## Builders and remote improvements: 2026-10-14

Construction now requires a reusable Builder on the destination tile. Builders cost 3 Gold, move two, can construct after moving, and spend their remaining turn on one improvement or road. They cannot attack, retaliate or capture cities. Construction can use any reachable land independently of fixed city territory. Improvements belong to their builder's player, connect to the nearest owned city and divide their base yield by tile distance. Enemy occupation blocks production; occupying units can claim an enemy improvement on their following turn. Capturing cities reroutes existing improvements.

- Feature commits: `132c4ab` and `21d210c`, integrated with the latest website loading/cache/navigation changes. Export source hash: `5b99f6a4ac5e842ed686edb62f2d5eba91b70416266d8121bf4a3f99ebcdd46b`. The 25.47 MiB export passed its credential audit.
- All 79 focused Unity cases, 25 standalone Poly cases, 101 web cases and 19 release-tool cases passed. After the connection contrast correction, all four focused native codec/scene/presentation cases passed again. Tests cover Builder legality, atomic research/build bills, exact fractional carry, nearest-city ties/rerouting, improvement capture, civilian combat, heuristic Builder use, migration and save isolation.
- Chromium acceptance used the actual export, admission/session code and synthetic identities. The v2 checkpoint reopened at round 11, Gold 1, Food 7/8, city level 2 and the same Warrior position. Loading did not settle income or growth.
- Simulated touch recruited a Builder for 3 Gold, moved it onto a forest and built a hunting hut. The same Builder then reached tile 35, outside every city's territory, and built an orchard connected to City 1 at distance 2. Its effective production was 0.5 Food/turn. Resource/terrain remained intact. The following owner turn accumulated Food to 7.5/10 and total city production to 5.5 Food/turn.
- Save & Menu, full page reload and continuation restored round 15, Gold 25, income 7, Food 7.5, researched technology, unit positions and health, including the Builder damaged by the heuristic opponent. The final export retained that checkpoint and its ready construction budget.
- The final connection lines visibly render above buildings and below ordinary fog. Unit → city → unit touch cycles passed at 390×844, 375×667 and 844×390. Screenshots cover recruitment, construction previews, reduced yields, connection lines, city growth, landscape and 1440×900 desktop. No gameplay scrollbars were introduced.
- Browser gameplay is synthetic Chromium acceptance. Real authenticated owner gameplay on the hosted release and physical iPhone/Safari acceptance remain separate checks.
