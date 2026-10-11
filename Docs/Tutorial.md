# Tester tutorial

The main menu's **Learn to play** button opens a dedicated 7×7 practice scene. It uses Warrior, Rider and Archer under the same `CoreThree` rules as the current single-player website. Practice takes place locally; it does not start a multiplayer match or change the frozen learned opponent.

## Player flow

An opening card identifies the blue player, shows the capture objective and highlights the existing Feedback button. Nine short lessons teach movement/exploration, recruitment, Warrior combat, ending a turn/income, Rider movement, Rider combat, Archer ranged combat, Archer retreat and city capture. Numbered tap signs, a blue origin, a gold target and connecting arrows guide the action on the actual board. Recruitment points to the real Warrior button after the city opens. Short result signs show effects such as −1 HP, +1 gold and victory; the card needs only one short sentence.

The lesson card has at most two buttons: **Show me** and **Continue**. Show me selects the unit/city and demonstrates the real action, then stays on the lesson; it becomes **Try again** to restore a hands-on exercise. Continue advances a finished lesson, or demonstrates an unfinished action before advancing. A player can therefore click through the whole course without reading or issuing board commands. **Exit tutorial** stays at the top. The duplicate gameplay Menu control is hidden, and the game's real Next control appears only in the turn lesson. The completion card offers Back to menu and Feedback.

Each lesson and demonstration begins from a fixed position so an unexpected move or recruit cannot make the tutorial permanently impossible. Demonstrations use the normal seat-based legal-action service and scene command adapter, with selection and result pauses. Extra board/recruit/turn input is blocked while demonstrating; Exit and Feedback remain available. The practice opponent attacks once during the turn lesson using shared rules, then returns the turn to the player. Other lessons give the tester time to act without an autonomous opponent interrupting them.

Warriors move one tile and attacks end movement. Riders commit one move of up to two tiles, may attack after moving and cannot move after attacking. Archers have vision/range two under CoreThree; moving first prevents an attack, while shooting first still permits a one-tile retreat. All three recruits cost two gold. Current combat deals one damage per attack, and moving onto an enemy city wins. These instructions describe the current rules rather than changing them.

## Feedback and saved games

The same Feedback button and form serve normal play and the tutorial. Tutorial reports carry `mode: Tutorial` and a stable `screen` such as `Tutorial / ArcherAttack`, captured when the form opens. The browser form displays this context. The optional Give feedback action on the completion card opens that form. During an open form the tutorial's controls and scripted opponent wait; the report's context does not change with later actions.

The dedicated scene explicitly enables the existing external-match boundary, which disables disk saves/autosaves. It does not load a normal save or write to Continue. Returning to the menu or reloading the website preserves normal saved matches. A local completion preference is separate from established game saves and PBp formats. Intermediate tutorial progress is not persisted; it restarts on a new visit.

## Implementation and release

`TutorialController` owns progression, demonstrations and scene setup; `TutorialUITKView` owns the two-control cards and fits the board between actual HUD/panels; `TutorialVisualGuide` places board signs and arrows. `TutorialLesson` provides deterministic fixtures and successful-command completion checks. The `Tutorial.unity` scene explicitly wires the existing input, cities, units, HUD, camera and tutorial document in the Inspector. Normal scenes retain their existing references. On desktop the lesson stays beside the board and gameplay panels; portrait mode stacks the compact lesson above the board and keeps 44-pixel tutorial touch targets. The practice camera stays fitted so targets cannot be panned off screen.

Build the existing authenticated website with its frozen three-unit opponent:

```sh
python3 Tools/WebRelease/build.py --multiplayer --learned-ai --output Build/TutorialWeb
```

The builder includes the tutorial scene and identifies its roster, board and lesson count in `release.json`. Review/audit this artifact before deploying it through the existing preview and production Workers. Access control, multiplayer storage/protocol and training remain separate from the tutorial.

## Validation

Focused Unity checks exercise every lesson through real legal actions, the opponent turn and income, retry/restart, wrong actions/recruitment, three-unit recruitment, victory and the save-disabled scene. A runtime course also verifies Show me, Try again and Continue through every lesson without a board command, with only two card controls. The touch UI guard raycasts each touch at its actual position, avoiding a cached mouse hover on the welcome button from blocking board taps. Touch collection uses the Input System's EnhancedTouch history to preserve a press and release that arrive between game updates. Gesture duration uses touch event timestamps, keeping short taps valid when rendering is slow. Runtime checks inject real Input System events for quick and ordinary taps, UI-origin touches, drags, long presses and cancellation.

Browser acceptance additionally needs actual taps/clicks through the full lesson sequence, feedback context/draft/screenshot/send behavior, normal Continue across menu return/reload, and desktop/phone-sized layout. Physical iPhone/Android acceptance remains separate from browser emulation.
