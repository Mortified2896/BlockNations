# Single-player browser release

The hosted playtest target is `https://blocknations.moneymattersmedia.com`.
It requires Google sign-in and tester approval; the hostname is publicly reachable, but the game files are protected.
`https://staging.blocknations.moneymattersmedia.com` is reserved for internal development.
These are distinct release targets; publishing the public game must not overwrite the staging service or its saved matches.

The browser release runs the game and Normal, Rider Focus, and Hard locally on the player's device.
The local training worker, Python, training spectator, native policy playtest, and external model/API experiments are excluded.
A trained-policy browser mode requires separate inference and device validation before release.

## Build isolation and credentials

Run from the repository on this Mac:

```sh
python3 Tools/WebRelease/build.py
```

The script uses the Editor version in `ProjectSettings/ProjectVersion.txt` and requires that version's Web Build Support module.
The snapshot must be outside the repository and either empty or marked as an owned web snapshot.
Generated output must be a subdirectory of `Build/`; the script rejects source paths before it clears old output.
It copies Assets, Packages, and ProjectSettings into an isolated project outside the working checkout.
It excludes the `Resources/PbpTransportSettings.asset` credential, local training scenes/code/models, and the Unity AI Assistant and ML-Agents packages.
UserSettings, local trainer files, and deployment credentials are never copied into the snapshot.
The working project, multiplayer settings, running trainer, and supported save formats are preserved.

The build uses `BLOCKNATIONS_SINGLE_PLAYER_WEB` to disable multiplayer initialization and UI entry points.
The transport cannot resolve a PBp URL or credential in that build.
The HTML fits the browser viewport; Unity renders at a maximum device pixel ratio of two.
`autoSyncPersistentDataPath: true` persists local saves to the browser's IndexedDB storage across page reloads.
Saves belong to that browser and hostname; clearing site data or switching browsers does not transfer them.
Gzip with Unity decompression fallback produces `.unityweb` files without requiring special server Content-Encoding configuration.

Only the generated `Build/WebRelease` directory is uploaded.
The audit reads exported bytes, decompresses gzip payloads, checks known PBp credentials and common private/API key signatures, rejects private-file paths, and enforces Cloudflare's 25 MiB per-asset limit.
Credential values are never printed by the audit.
The Content Security Policy permits network connections only to the game's own origin.

## Cloudflare release

`Web/wrangler.jsonc` is a separate Workers Static Assets preview.
`Web/wrangler.production.jsonc` targets the public hostname.
Wrangler authentication belongs to the developer machine, outside the game and repository.

```sh
cd Web
npm ci --ignore-scripts
npm run deploy:access
npm run deploy:preview
# After preview acceptance, publish the same generated artifact:
npm run deploy:production
```

Before publishing: validate the actual export, inspect browser/network behavior, inventory existing DNS/Worker routes, and save a private rollback receipt.
Verify the preview can start a match, recruit, move, end a turn, and receive local AI moves.
Then publish the exact reviewed artifact and verify public HTTPS, assets, gameplay, and the unchanged staging mapping.
Desktop browser and emulated mobile acceptance are separate from tests on physical iPhone/Android devices.

Static assets are served through an approval-checking Worker. Authentication and private admission storage use Workers and SQLite-backed Durable Objects, which are available on Cloudflare's free plan within its limits. No server-side inference is required.
All game responses receive `X-Robots-Tag: noindex, nofollow, noarchive`; this is indexing guidance in addition to authentication.

## Tester admission and Review accounts

The two sites share Google identity, with **one-way approval inheritance**:

| Account state | BlockNations | Article Review |
| --- | --- | --- |
| Approved reviewer | Automatic game access | Existing reviewer access |
| Approved only for the game | Game access | Requires separate Review approval |
| New Google account | Waiting for game approval | Requires separate Review approval |
| Game-specific rejection/disable | Game files denied, including for reviewers | Review approval unchanged |

The owner manages game requests at `https://blocknations.moneymattersmedia.com/admin/testers`.
Sign in through `/access` first if the browser has no game session.
Only a currently approved Review administrator can manage that queue.
Game approval changes only `game_testers` in the private `blocknations-access` Durable Object namespace. It never upgrades the Review account's status or role.
Review-only approval grants automatic game access; removing it removes inherited game access unless the person has a separate game approval.

`Web/access/worker.mjs` runs before **every** static asset in production and preview, including HTML, the loader, framework, data, Wasm, release metadata and alternate paths.
Anonymous visitors see the sign-in page; other unauthorised game-file requests receive 403.
Protected responses use `private, no-store`. Auth/storage failures return 503 and never fall back to public assets.
Revocation blocks subsequent requests. A client that already downloaded the game can continue running its local code until reload; website authentication cannot recall downloaded bytes.

The sign-in handoff runs on `feedback.moneymattersmedia.com/blocknations-auth/*`; preview uses `/blocknations-auth-preview/*`.
An existing Review session is reused automatically after the player selects **Continue with Google**. Otherwise the existing Review Google login handles authentication, using its already registered OAuth callback.
The browser returns to the game with a 90-second, one-use opaque code tied to the initiating host-only cookie and exact game origin. The callback consumes it atomically and removes it from the URL immediately.
The game session cookie is encrypted, `Secure`, `HttpOnly`, `SameSite=Lax` and host-only. Preview and production share admission decisions, with separate browser cookies.
The Google credentials stay in Review. The game has no binding to the article database; it calls only the Review authentication/session APIs. Review session cookies stay in private server storage, outside game assets and browser responses.
Sign-out from BlockNations revokes its server session and clears its cookies while leaving Review signed in. Review sign-out or expiration invalidates the linked game session on its next request.

`Web/wrangler.access.jsonc` owns the private admission/session storage and has no public URL, preview URL or route.
Deploy it before either game Worker. Install the **same** high-entropy `SESSION_SECRET` in production and preview through Wrangler's hidden-input secret workflow before publishing the gate; missing secrets fail closed.
The initial secret is kept outside Git at `~/.config/blocknations/access.json`, directory mode 700 and file mode 600. Never print it or put it in the export.
This integration does not require a new Google OAuth client, parent-domain cookies, Cloudflare Access configuration, or a paid-plan change.
The local regression suite uses synthetic Review identities and real isolated Durable Object SQLite storage; it needs no live credentials.

## Regression checks

```sh
(cd Web && npm test)
python3 -m unittest discover -s Tools/WebRelease -p 'test_*.py'
python3 Tools/WebRelease/audit.py Build/WebRelease
```

Store build logs, deployment receipts, and browser acceptance captures in ignored local output.
Never put Cloudflare credentials, PBp credentials, or private training/human match data into release files or documentation.

## Initial release acceptance: 2026-10-09

- Public Worker: `blocknations-web`, version `371e089b-2517-405c-bc80-50b85f0c2780`.
- Preview: `https://blocknations-web-preview.johannes-gaebler.workers.dev`, version `f23f769f-24c5-4773-ba28-deba2203de57`.
- Raw files downloaded with curl over public HTTPS matched the reviewed HTML, release metadata, loader, framework, data, and Wasm bytes.
- A public Chromium match completed Hard's turn and returned control at round two with player gold four. Network inspection showed no external model requests. Cloudflare attempted to load its Web Analytics beacon, which the release Content Security Policy blocked.
- Export: 24.83 MiB total; largest asset 13.71 MiB. Export audit found zero credential matches, including checks inside decompressed payloads. Separate comparisons against five local Codex/Cloudflare authentication tokens also found zero matches.
- Local and preview Chromium verification covered Hard on 11×11, recruiting a Rider, moving, panning the camera, completing the AI turn, and reloading/continuing a saved match. A 390×844 touch viewport with device pixel ratio two could select Hard and start a game.
- The saved match reopened at round two with the same player gold after a full preview page reload.
- Cloudflare's public custom domain replaced only the public A record. Staging retains its DNS-only A record to `91.98.79.206`, TTL 600 seconds, and the existing backend. Other Worker custom domains were preserved.

Physical iPhone/Android devices and Safari are still separate acceptance work.
At a 390-pixel-wide viewport with device pixel ratio one, the existing Unity settings pane can clip its heading and needs more scrolling; the tested ratio-two touch view displayed the heading and Start button correctly.
The browser playtest includes the existing local AI opponents; the recently trained policy is not included in this release.

## Menu follow-up: 2026-10-09

- Public Worker version: `cb3f824c-0b63-4fa9-9b75-0805da24b62d`; preview version: `01c880fe-8229-4923-8f31-32f6d3ebe4e3`.
- Main-menu button backgrounds reach both horizontal edges. Verified in Chromium at 1280×720 and 976×1622.
- Continue uses the saved match's terminal state, with shared eligibility for button visibility and its action. Completed primary saves suppress older legacy saves; supported unfinished legacy saves still work. Saved files are preserved.
- Normal single-player city captures persist the finished status immediately. A controlled 11×11 browser position was continued and won by capturing the enemy city; IndexedDB contained `gameOver: true`, and Continue stayed hidden after full page reloads in both layouts.
- All seven focused Unity EditMode save eligibility cases passed. The rebuilt export audit and comparisons against five local authentication values found zero credential matches.
- The public release uses the same artifact tested on preview. Build/test receipts and browser captures are kept under ignored `Logs/MenuFix/` and `output/playwright/`.

## Google sign-in and approval acceptance: 2026-10-09

- Production Worker version: `8f62045a-e0d1-4a38-873e-bcbedf2e27f3`; preview version: `6794ef8b-2250-4b22-aa39-857246ddfdb7`.
- Private admission Worker version: `fb5f42dd-18b5-45e3-af57-f6b039ffa952`, with no public URL or routes.
- All 17 authentication/admission regressions passed, including one-way approval, game-only approval leaving Review pending, disabled access, live Review revocation, expired sessions, bound one-use handoffs, admin origin checks, logout and unavailable services failing closed.
- Real Chrome verification on preview and production reused an existing approved Review session and reached the Unity main menu without another Google login or approval. The approved Review administrator could open the separate game approval queue.
- Anonymous HTTPS checks on both targets returned sign-in pages for `/` and `/index.html`, and 403 for the actual loader, framework, data, Wasm, release metadata and admin queue. Responses used `private, no-store`.
- A fresh browser session reached the existing Google sign-in flow. A complete new-person Google login was not performed; pending registration and game-only approval were verified with synthetic identities in the regression suite.
- The sign-in page was visually checked at desktop 1280×800 and mobile 390×844. Physical phones and Safari remain separate acceptance work.
- Deployment reused the exact previously published game assets from version `cb3f824c-0b63-4fa9-9b75-0805da24b62d`, with no updated static files to upload. The preserved export passed the credential audit. Staging still resolves to `91.98.79.206`, and Review authentication remains ready.
