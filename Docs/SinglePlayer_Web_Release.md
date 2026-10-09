# Single-player browser release

The public playtest target is `https://blocknations.moneymattersmedia.com`.
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
npm run deploy:preview
# After preview acceptance, publish the same generated artifact:
npm run deploy:production
```

Before publishing: validate the actual export, inspect browser/network behavior, inventory existing DNS/Worker routes, and save a private rollback receipt.
Verify the preview can start a match, recruit, move, end a turn, and receive local AI moves.
Then publish the exact reviewed artifact and verify public HTTPS, assets, gameplay, and the unchanged staging mapping.
Desktop browser and emulated mobile acceptance are separate from tests on physical iPhone/Android devices.

Static assets use Cloudflare's free static hosting allowance. No server-side inference is required.
Preview URLs receive `X-Robots-Tag: noindex, nofollow`; this is indexing guidance, not authentication.

## Regression checks

```sh
python3 -m unittest discover -s Tools/WebRelease -p 'test_*.py'
python3 Tools/WebRelease/audit.py Build/WebRelease
```

Store build logs, deployment receipts, and browser acceptance captures in ignored local output.
Never put Cloudflare credentials, PBp credentials, or private training/human match data into release files or documentation.

## Release acceptance: 2026-10-09

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
