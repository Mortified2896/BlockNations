# Poly land playtest

Poly is an owner-only Vs AI mode in the Block Nations website. Choose **Poly 11×11** from the main menu. It continues your separate Poly match automatically; **New match** asks before replacing that continuation slot. Unity Editor access is also available. Website launch permission is returned by the verified account session only when the approved administrator matches `POLY_OWNER_USER_ID`.

## Playing

- Start on an 11×11 seeded land map with one city and a Warrior. The opponent has the same starting position resources, with one extra opening Gold when its first turn begins. Five neutral villages provide expansion through capture.
- Tap your unit, then a yellow reachable tile or red attack target. Drag the map and pinch to zoom using the normal Block Nations camera. On an occupied city, the first tap shows the unit; repeated taps alternate unit → city → unit. Occupied cities open compact stats instead of unavailable recruitment cards; a Unit tab also returns to the stationed unit. Landscape inspection sits beside the board. Tap an empty city for recruitment or a resource for development. **Tech** opens an illustrated tree with round nodes, dependency arrows and selectable branches. Recruitment and city development use page buttons; there are no map or gameplay-panel scrollbars.
- Spend Gold on technology, recruitment and permanent tile improvements. Food from improvements grows city population every owner turn. Population is not consumed by recruitment.
- Each city has a permanent 3×3 territory. Capturing villages grants their existing territory. Leveling never expands borders.
- City levels require 2, then 3, then 4, and successively more population. Surplus carries forward. Each level adds one Gold per owner turn and queues a reward choice.
- Level 2 offers Workshop (+1 Gold/turn) or Fortifications (city defense). Level 3 offers 5 immediate Gold or 2 population. Level 4 offers another Workshop or Fortifications. Level 5 onward offers a Giant or 10 Gold. A Giant appears on an empty city/territory tile; if there is no room, the economic reward remains available.
- Cities support `level + 2` living units recruited there. Recruitment needs an empty city and is limited to once per city per owner turn. Recruits and reward Giants become ready on the following owner turn.
- Enter a village or enemy city, then capture it on your following turn while standing there with unused actions. Taking the opponent's last city wins. Other captures continue the match.
- Rest an unused unit to heal 4 HP in friendly territory or 2 HP elsewhere. Surviving defenders retaliate when the attacker is in their range. Catapults cannot retaliate or fire after moving. Riders may retreat after attacking; Knights can attack again after a kill.

The unit roster is Warrior, Archer, Rider, Defender, Swordsman, Knight, Catapult and Giant. Balance values are provisional Poly values. There is no water, Mind Bender, Cloak, science resource or AI training in this rules version.

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

Improvements retain their resources and terrain. Every improved tile in a city's fixed territory contributes automatically; no worker assignment or food upkeep is required. Green grain badges and Gold coin badges show numeric yields on resource tiles; the city badge shows its total Gold income. Inspection shows current yields, improvement gains and prices. A city's growth meter shows stored Food and turns until its next population point.

| Improvement | Build cost | Permanent yield per owner turn |
| --- | ---: | --- |
| Orchard on fruit | 2 Gold | 1 Food |
| Hunting hut on animals | 2 Gold | 2 Food |
| Farm on crops | 5 Gold | 3 Food |
| Lumber hut in forest | 3 Gold | 1 Gold |
| Mine on ore | 5 Gold | 2 Gold |
| Windmill | 5 Gold | 2 Food per adjacent farm |
| Sawmill | 5 Gold | 1 Gold per adjacent lumber hut |
| Forge | 5 Gold | 2 Gold per adjacent mine |

A tile supports one improvement plus a road. A city supports one of each windmill, sawmill and forge; these can use an unimproved plains tile when their matching neighbours exist. Adjacency includes the eight surrounding cells in the same city territory. Building another matching improvement immediately increases the hub's yield; the tile purchase preview includes that gain.

At the start of each owner turn, Food is collected once. Each `4 + 2 × city level` Food produces one population point, with surplus retained. Population thresholds for levels remain 2, 3, 4 and so on; level rewards remain unchanged. Gold income is `city level + 1 + workshops + tile Gold yields`, collected once after growth. Opening Gold remains 7 for the human and 8 for the opponent including its first income. Roads cost 2 Gold, have no economic yield, and remove rough-terrain movement costs. Mountains require Climbing; forests/mountains cost two movement points, with an adjacent passable tile always reachable using a unit's one movement action.

## Fog and heuristic opponent

Poly uses `MatchEngine.Visibility` from ordinary gameplay. Visibility is live, can shrink after a spotter leaves, and is separate from remembered terrain. Archers retain the normal two-tile vision; other Poly units and cities see one tile. Unseen units are hidden. The map remembers only improvements and city metadata actually observed, rather than displaying current hidden state.

`PolyOpponent` receives a seat observation containing its own resources/cities, its own and currently visible units, remembered terrain, and legal actions. It grows cities, buys useful technology, recruits, explores, heals, defends and captures through deterministic scoring. The policy receives neither the full match nor the human treasury or hidden pieces. It runs locally and resumes an interrupted saved AI turn without applying income again.

## Save and compatibility boundary

`poly-land-v2` adds persistent city Food. The loader also supports `poly-land-v1`: levels, population progress, treasury, turn budgets and fog remain intact; old harvested fruit/animals become orchards/hunting huts, and existing farms/mines/lumber huts gain recurring yields. Migration uses the original map seed and only converts already remembered tiles in fog memory. Loading does not settle Food or income, and the old checkpoint is preserved until the next action saves it. `save_poly_v1_<account scope>.json` is distinct from standard single-player saves and PBp snapshots. It contains the map, city progression, resources, researched technology, live unit/action budgets, per-seat fog memory, current turn and outcome. The codec validates before writing; unsupported/corrupt saves remain available for inspection. The previous checkpoint is retained separately. Browser writes explicitly flush IndexedDB before the saved indicator confirms success or Save & Menu leaves.

Continuation is local to the same browser/device and account. It is not a cloud save or synchronization between devices.

The Poly scene explicitly wires the normal GridManager, CameraController, TileHoverManager, UnitSelectionManager, top HUD, city/unit state managers, tile/city/unit prefabs and New Input System event module. Poly projects its authoritative state into native sprites, world coordinates and fog overlays. The standard Warrior, Rider and Archer sprites are reused; additional units and terrain have small pixel graphics rendered on the same white tiles. PolyGameplayUITKView adds compact bottom sheets and a paged roster, with branch-based research in a modal. It contains no ScrollView or UI grid. LegalActionService and SceneSimulationAdapter dispatch only explicitly wired Poly scenes to the Poly rules. The standard unit registry, learned policy catalog, normal save format and PBp protocol remain compatible. Existing poly-land-v1 checkpoints keep the same account-scoped filename and migrate into the new economy without advancing a turn.

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
