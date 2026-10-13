# Single-player browser release

The hosted playtest target is `https://blocknations.moneymattersmedia.com`.
It requires Google sign-in and tester approval; the hostname is publicly reachable, but the game files are protected.
`https://staging.blocknations.moneymattersmedia.com` is reserved for internal development.
These are distinct release targets; publishing the public game must not overwrite the staging service or its saved matches.

The prepared three-unit browser release runs the game and a single frozen learned opponent locally on the player's device. New Play vs AI games use Warrior, Rider and Archer; the AI selector is removed. Its frozen model is independent of the continuing Mac training workload. See the [learned-AI release record](Learned_AI_Browser_Playtest.md) for current validation and publication status.
The [Google-account multiplayer release](Web_Multiplayer_Playtest.md) adds asynchronous two-person 7×7 matches, friends, in-game invitations and opt-in Web Push. Build the combined three-unit single-player candidate with `python3 Tools/WebRelease/build.py --multiplayer --learned-ai --output Build/CoreThreeWeb`. The account build without `--learned-ai` preserves the legacy local opponents; the unqualified command below builds the historical separate learned-AI candidate with development difficulty presets.
The local training worker, Python, training spectator, native policy playtest, and external model/API experiments are excluded from both releases.

## Build isolation and credentials

Run from the repository on this Mac:

```sh
python3 Tools/WebRelease/build.py
```

The script uses the Editor version in `ProjectSettings/ProjectVersion.txt` and requires that version's Web Build Support module.
The snapshot must be outside the repository and either empty or marked as an owned web snapshot.
Generated output must be a subdirectory of `Build/`; the script rejects source paths before it clears old output.
It copies Assets, Packages, and ProjectSettings into an isolated project outside the working checkout.
It excludes the `Resources/PbpTransportSettings.asset` credential, local training scenes/code/models, and the Unity AI Assistant and ML-Agents packages. The frozen `Resources/LearnedAI` model/catalog and direct Inference Engine dependency remain for local browser inference.
UserSettings, local trainer files, and deployment credentials are never copied into the snapshot.
The working project, multiplayer settings, running trainer, and supported save formats are preserved.

The build uses `BLOCKNATIONS_SINGLE_PLAYER_WEB` to disable multiplayer initialization and UI entry points.
The transport cannot resolve a PBp URL or credential in that build.
The HTML fits the browser viewport; Unity renders at a maximum device pixel ratio of two.
`autoSyncPersistentDataPath: true` persists local saves to the browser's IndexedDB storage across page reloads.
Saves belong to that browser and hostname; clearing site data or switching browsers does not transfer them.
Release builds use Brotli with Unity decompression fallback disabled, and size-focused Wasm optimization (`DiskSize`). The access Worker supplies `Content-Encoding: br` for `.data.br`, `.framework.js.br`, and `.wasm.br`, with `application/wasm` for code so the browser can compile it during download. It also supports older gzip `.gz` and `.unityweb` exports. Compressed bodies use Workers' `encodeBody: "manual"` to prevent double compression. These headers apply only to successful, authorized game-file responses; denial and missing-file responses keep their normal encoding.

Unity's automatic IndexedDB data caching stays disabled. Content-hashed files under `/Build/` use `private, no-cache, must-revalidate`. The delivery service worker retains only these static files in a bounded cache (at most eight files, enough for two complete releases), because browser HTTP caches can skip large Wasm/data files. Every retained-file use first makes a fresh, uncached HEAD request through the admission Worker and compares its ETag. Offline/error checks fail without a cached fallback; denied or changed versions discard that retained entry. Unavailable browser storage falls back to ordinary online downloads. The template installs/activates the worker before large first-visit downloads, with a bounded wait so unavailable service workers do not stall startup. Existing push behavior is preserved.

Static game bytes can remain in that browser's local cache. Game HTML, account pages, APIs, release metadata and saves are not cached and remain `private, no-store`. As with any already downloaded game, a running client cannot have its in-memory code recalled by logging out; fresh loads and cached-file access still require server admission. A plain static-file server needs equivalent encoding/MIME headers to run the new export; opening it from disk or using an unconfigured Python HTTP server will fail.

Only the generated `Build/WebRelease` directory is uploaded.
The audit reads exported bytes, decompresses gzip/Brotli payloads (Brotli auditing uses the Node.js runtime required for web deployment), checks known PBp credentials and common private/API key signatures, rejects private-file paths, and enforces Cloudflare's 25 MiB per-asset limit.
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

Protected responses use `Referrer-Policy: same-origin`. Chrome can send `Origin: null` for form POSTs from `no-referrer` documents, which conflicts with the exact-origin check on tester decisions, logout and account switching. The same-origin policy preserves those form submissions while withholding referrers from other sites; missing, null and foreign mutation origins remain rejected. The 2026-10-12 fix passed all 58 web tests and a live Chrome tester approval that persisted after reload.

`Web/access/worker.mjs` runs before **every** game asset in production and preview, including HTML, the loader, framework, data, Wasm, release metadata and alternate paths. The install manifest, two icons and delivery/navigation/push scripts are public shell assets; they contain no game data or credentials.
Anonymous visitors see the sign-in page; other unauthorised game-file requests receive 403.
Protected dynamic responses and errors use `private, no-store`; successful content-hashed build files require private server revalidation on every reuse. Auth/storage failures return 503 and never fall back to public assets or cached game responses.
Revocation blocks subsequent requests. A client that already downloaded the game can continue running its local code until reload; website authentication cannot recall downloaded bytes.

The sign-in handoff runs on `feedback.moneymattersmedia.com/blocknations-auth/*`; preview uses `/blocknations-auth-preview/*`.
An existing Review session is reused automatically after the player selects **Continue with Google**. Otherwise the existing Review Google login handles authentication, using its already registered OAuth callback.
The browser returns to the game with a 90-second, one-use opaque code tied to the initiating host-only cookie and exact game origin. The callback consumes it atomically and removes it from the URL immediately.
The game session cookie is encrypted, `Secure`, `HttpOnly`, `SameSite=Lax` and host-only. Preview and production share admission decisions, with separate browser cookies.
The Google credentials stay in Review. The game has no binding to the article database; it calls only the Review authentication/session APIs. Review session cookies stay in private server storage, outside game assets and browser responses.
Sign-out from BlockNations revokes its server session and clears its cookies while leaving Review signed in. Review sign-out or expiration invalidates the linked game session on its next request.

The browser game includes an **Account** link beneath the canvas. It opens `/access` in another tab so the player can manage sign-in without leaving the running game. Approved, pending and disabled testers have a **Switch Google account** button there; the signed-out page also offers **Use a different Google account**.
Switching is an origin-checked POST that revokes the current game session and starts a fresh browser-bound sign-in attempt. It deliberately skips warm Review-session reuse on the first broker visit and sends `prompt=select_account` only for this explicit action. Ordinary sign-in keeps reusing valid sessions. A successful Google callback replaces the Review identity in this browser too; game and article approvals remain separate. Cancelling or failing the switch leaves the game signed out, with a manual retry.

`Web/wrangler.access.jsonc` owns the private admission/session storage and has no public URL, preview URL or route.
Deploy it before either game Worker. Install the **same** high-entropy `SESSION_SECRET` in production and preview through Wrangler's hidden-input secret workflow before publishing the gate; missing secrets fail closed.
The initial secret is kept outside Git at `~/.config/blocknations/access.json`, directory mode 700 and file mode 600. Never print it or put it in the export.
This integration does not require a new Google OAuth client, parent-domain cookies, Cloudflare Access configuration, or a paid-plan change.
The local regression suite uses synthetic Review identities and real isolated Durable Object SQLite storage; it needs no live credentials.

## Keeping the game loaded between modes

Local modes use Unity scene transitions. The Google-account browser build also keeps the Unity instance alive when opening the existing HTML multiplayer lobby, returning to the menu, and opening another online match. The lobby is an approved same-origin frame; all other account and administration pages retain their framing restrictions. Account management opens in a separate tab.

The frame can request only a validated match ID through the registered main-menu component. That component refreshes the authenticated session and retrieves the match again; membership, seats, protocol version and waiting-lobby rules remain server-controlled. A waiting lobby never leaves a pending online launch that could override a subsequent local game. Leaving an online board uses the existing local-save and main-menu scene path, and ends its live match watch.

An expired or revoked session still uses top-level navigation for sign-in. Direct lobby links and a first visit to an online match retain their standalone behavior. The static delivery cache avoids repeat downloads, but a deliberate page reload still has to initialize Unity.

## Regression checks

```sh
(cd Web && npm test)
python3 -m unittest discover -s Tools/WebRelease -p 'test_*.py'
python3 Tools/WebRelease/audit.py Build/WebRelease
```

Store build logs, deployment receipts, and browser acceptance captures in ignored local output.
Never put Cloudflare credentials, PBp credentials, or private training/human match data into release files or documentation.

## Browser delivery and navigation acceptance: 2026-10-14

- Loading/navigation commits: `a8a8dee` and `4164339`, merged and pushed to `main`. The export includes the current Poly `poly-land-v2` changes and the live-multiplayer update. Native source hash: `d2f3376e35a9132d63c08d95ee2c1dfaa2ae24766cfb41d996693cd0ed815dfd`.
- Production Worker: `3977bf02-a3ba-4177-9901-5e0eced2c6c5`, verified at 100% traffic. Preview: `2e101ef4-0f89-44c5-8c60-c27a6ab82bee`. Both received the same reviewed export. Public `sw.js`, `PWA.js`, and `Navigation.js` matched the local files byte-for-byte on both targets. Anonymous loader, Wasm, release metadata and multiplayer-session requests remained 403 with `private, no-store`. Existing non-asset production bindings were preserved.
- The 14-file export is 25.39 MiB, with a largest file of 15.99 MiB. Its four game build files total 25.35 MiB versus 31.11 MiB in the current gzip/fallback control: approximately 19% less transferred game data. The credential audit found zero matches against one known local credential.
- All 101 web checks and 19 release-tool checks passed. Workers HTTP-runtime validation confirmed that Brotli is decoded exactly once, with the Wasm MIME type and body-free HEAD responses. The generated artifact's native source hash and each delivery script matched source before publication.
- The controlled Chromium comparison used a 390×844 viewport, a shared server-side 10 Mbps download limit, 150 ms response latency and 4× CPU slowdown. Server throttling also covered service-worker requests. Engine initialization measured 30.9 seconds for the control, 24.9 seconds for the candidate, and 3.0 seconds for a retained-file repeat. A separate visual probe included the Unity splash: the first visible menu measured 33.5 → 28.9 seconds; repeat visits measured 35.5 → 6.0 seconds. These are local synthetic-account samples, not production or physical-phone timings.
- The repeat transferred only access-check responses for the large game files. Removing browser cookies returned 403 for a retained Wasm file. Stopping the local delivery server prevented a retained data file from loading. Storage failure, changed ETags, denied/expired access and bounded cache eviction are covered by the regression suite.
- Actual canvas and lobby clicks covered waiting-lobby → local AI, a completed learned-opponent turn, online match entry/exit, browser Back, Advanced and Poly save/reopen. The Unity instance and browser document stayed identical across the transitions; the online round trip made zero new build-file requests and stopped the previous live match watch. A same-origin frame request for another account's private lobby was rejected without starting a board.
- Chromium and WebKit phone-sized lobby round trips passed. Desktop acceptance at 1280×800 and device pixel ratio two covered the complete menu and online round trip. Same-origin app installation and notification permission use the top-level document; synthetic permission tests cover the framed setup. No real Web Push message was sent.
- Existing inspection limits: at 1280×800 with pixel ratio one and Continue visible, the menu can extend below the canvas; the UITK layouts are unchanged. WebKit reported CSP style-attribute warnings around native dialog elements, while the nonced stylesheet, new-game dialog and notifications tab remained functional. A real approved hosted session and physical iPhone/Android acceptance remain separate checks.
- Build, benchmark, deployment and gate receipts are under ignored `Logs/WebRelease/`; screenshots are in the owned local loading-QA directory. The frozen learned model remains independent of live Mac training.

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

## MMM branding follow-up: 2026-10-09

- The shared sign-in, account, pending, unavailable and admin pages use MMM's minimal editorial style: white background, grayscale text, serif wordmark/headings, thin dividers and black primary buttons.
- Production version: `81ec433f-6dba-4d1e-a9b9-6a645273b003`; preview version: `cc56f8f5-9116-4282-9cb6-abe780d99056`. Both deployments uploaded no changed game assets.
- All 17 access regressions passed. Desktop 1280×800 and mobile 390×844 captures were inspected; the live mobile sign-in page had no horizontal overflow and a 48-pixel primary button. The pending page was also inspected with a synthetic identity.
- Anonymous preview and production loader requests remained denied with 403. Captures are under ignored `output/playwright/blocknations-brand-*.png`.

## Phone background fill: 2026-10-09

- The game HTML's browser theme color, page/body background and loading overlay now use `#314d79`, matching the main menu's `rgb(49, 77, 121)`. This replaces the lighter `#22658c` fill visible around the game on the phone.
- Production version: `f752def9-425d-4b8f-959d-a61cc958caea`; preview version: `b47e2b5b-22c5-4f99-a4a5-2c62f1cb2568`. Only `index.html` changed in the published assets; every other preserved asset hash matched.
- Signed-in browser checks verified the theme metadata and computed backgrounds on both targets. The source WebGL template has the same values for future builds. Physical iPhone browser-bar tint remains a device-side check after reload.
