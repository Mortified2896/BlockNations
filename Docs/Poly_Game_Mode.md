# Poly land playtest

Poly is an owner-only Vs AI mode in the Block Nations website. Choose **Poly 11×11** from the main menu. It continues your separate Poly match automatically; **New match** asks before replacing that continuation slot. Unity Editor access is also available. Website launch permission is returned by the verified account session only when the approved administrator matches `POLY_OWNER_USER_ID`.

## Playing

- Start on an 11×11 seeded land map with one city and a Warrior. The opponent has the same starting position resources, with one extra opening Gold when its first turn begins. Five neutral villages provide expansion through capture.
- Tap your unit, then a green reachable tile or red attack target. The map supports scrolling and zoom. Tap a city for recruitment or a tile in its territory for development. **Technology** opens the branching research list.
- Spend Gold on technology, recruitment, resource harvesting and improvements. City population is progression from development, not a pool consumed by recruitment.
- Each city has a permanent 3×3 territory. Capturing villages grants their existing territory. Leveling never expands borders.
- City levels require 2, then 3, then 4, and successively more population. Surplus carries forward. Each level adds one Gold per owner turn and queues a reward choice.
- Level 2 offers Workshop (+1 Gold/turn) or Fortifications (city defense). Level 3 offers 5 immediate Gold or 2 population. Level 4 offers another Workshop or Fortifications. Level 5 onward offers a Giant or 10 Gold. A Giant appears on an empty city/territory tile; if there is no room, the economic reward remains available.
- Cities support `level + 2` living units recruited there. Recruitment needs an empty city and is limited to once per city per owner turn. Recruits and reward Giants become ready on the following owner turn.
- Enter a village or enemy city, then capture it on your following turn while standing there with unused actions. Taking the opponent's last city wins. Other captures continue the match.
- Rest an unused unit to heal 4 HP in friendly territory or 2 HP elsewhere. Surviving defenders retaliate when the attacker is in their range. Catapults cannot retaliate or fire after moving. Riders may retreat after attacking; Knights can attack again after a kill.

The unit roster is Warrior, Archer, Rider, Defender, Swordsman, Knight, Catapult and Giant. Balance values are provisional Poly values. There is no water, Mind Bender, Cloak, science resource or AI training in this rules version.

## Economy and technology

Research costs `tier × (4 + owned cities)` Gold and requires its parent. The technology list displays the current price and unlocks even when unaffordable.

| Branch | Unlocks |
| --- | --- |
| Organization → Farming → Construction | Fruit harvest; farms; adjacency windmills |
| Organization → Shields | Defenders |
| Hunting → Archery | Animal harvest; Archers and forest defense |
| Hunting → Forestry → Mathematics | Lumber huts; Catapults and adjacency sawmills |
| Climbing → Mining → Smithing | Mountain movement/defense; mines; Swordsmen and adjacency forges |
| Riding → Roads → Chivalry | Riders; roads; Knights |

Fruit/animal harvest costs 2 Gold and gives 1 population. Farms/mines cost 5 and give 2; lumber huts cost 3 and give 1. Each city supports one windmill, sawmill and forge, each costing 5 Gold and contributing 2 population per adjacent matching improvement in that same territory. Later matching improvements also contribute the adjacency bonus. Roads cost 2, add no population and remove rough-terrain movement costs. Mountains require Climbing; forests/mountains cost two movement points, with an adjacent passable tile always reachable using a unit's one movement action.

Income is `city level + 1 + workshops`, collected once at the start of its owner's turn. Opening Gold is 7 for the human and 8 for the opponent including its first income. No food, unit upkeep or demographic model applies to Poly.

## Fog and heuristic opponent

Poly uses `MatchEngine.Visibility` from ordinary gameplay. Visibility is live, can shrink after a spotter leaves, and is separate from remembered terrain. Archers retain the normal two-tile vision; other Poly units and cities see one tile. Unseen units are hidden. The map remembers only improvements and city metadata actually observed, rather than displaying current hidden state.

`PolyOpponent` receives a seat observation containing its own resources/cities, its own and currently visible units, remembered terrain, and legal actions. It grows cities, buys useful technology, recruits, explores, heals, defends and captures through deterministic scoring. The policy receives neither the full match nor the human treasury or hidden pieces. It runs locally and resumes an interrupted saved AI turn without applying income again.

## Save and compatibility boundary

`poly-land-v1` is an explicit save/rules identity. `save_poly_v1_<account scope>.json` is distinct from standard single-player saves and PBp snapshots. It contains the map, city progression, resources, researched technology, live unit/action budgets, per-seat fog memory, current turn and outcome. The codec validates before writing; unsupported/corrupt saves remain available for inspection. The previous checkpoint is retained separately. Browser writes explicitly flush IndexedDB before the saved indicator confirms success or Save & Menu leaves.

Continuation is local to the same browser/device and account. It is not a cloud save or synchronization between devices.

The Poly scene explicitly wires its UIDocument, UI asset and New Input System event module. Its code-native UITK board displays terrain, improvements, cities, units and legal-action markers. The ordinary match engine, standard unit registry, learned policy catalog, normal save format and PBp protocol do not acquire Poly rules.

## Validation

Run `dotnet test Tools/Poly/BlockNations.Poly.Tests.csproj` for rules, fog, transitions, heuristic visibility and save-state checks. Run Unity EditMode fixtures `PolyRulesTests|PolyNativeTests` with graphics for the actual Unity JSON codec, account-scoped save isolation, explicit scene/input/UI wiring and build registration. Run `npm test --prefix Web` for identity-gated session permission and existing web regressions.

The ordinary release builder includes Poly alongside existing scenes and records it in `release.json`. Prepare/build an owned isolated snapshot with `Tools/WebRelease/build.py --multiplayer --learned-ai`; audit the export, check browser gameplay and reload/continue, then deploy the same reviewed bytes. Physical phone behavior is a separate acceptance check.

## Release acceptance: 2026-10-13

- Feature commit: `1d92721`, merged and pushed to `main`. Export source hash: `f0a732911761c2ffaacefb1e14799d4d41e98b1d5f3b4917a31a0d05faca8f53`.
- Production Worker version: `bffa6836-a350-4a27-b2dc-21ec0b788a55`, serving 100% of production traffic. Preview version: `00d41eda-8b3f-4a41-9111-2a3a13d76baa`. Both use the same reviewed export.
- All 15 Unity Poly checks, 12 standalone Poly checks, 43 existing Advanced rules checks, 71 web checks and 17 release-tool checks passed. The 31.17 MiB export passed the credential audit; the staged patch and decompressed release also contained zero matches against two local authentication values.
- Chromium acceptance used synthetic accounts with the actual admission Worker, session API and exported Unity game. The owner menu launched Poly; the tester session received `canPlayPoly: false` and its rendered menu hid the entry. Desktop and 390×844 portrait checks covered the board, city sheet, scrollable unit cards and map focus after resizing.
- Actual canvas clicks moved a Warrior, bought Organization, harvested two resources, reached city level 2, selected Workshop, recruited a second Warrior and completed two opponent turns. After Save & Menu and a full page reload, continuation restored round 3, 2 Gold, income 4, city level 2 and the same two units/positions.
- Live production settings matched the configured owner identity and preserved every existing service, namespace, secret binding and origin variable. The active Worker contains the owner-gated session code. Anonymous production release/session requests remained 403 with `private, no-store`.
- A real signed-in owner session on production and physical iPhone/Android or Safari gameplay remain unverified. Browser acceptance above is local Chromium with synthetic identity, separate from the successful hosted deployment.
