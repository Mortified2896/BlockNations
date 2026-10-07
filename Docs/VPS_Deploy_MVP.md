# Block Nations VPS Deployment

The supported PBp backend is the root Node/Express `server.js` launched by `npm start`. This guide describes the existing VPS/PM2 workflow and client provisioning. It does not establish that the configured public server is currently deployed or reachable.

Last reviewed: 2026-10-07. For endpoints and known correctness limits, see [the HTTP contract](HTTP_PBp_Transport_Contract.md) and [development follow-ups](Development_Followups.md).

## Deployment assumptions

- Repository: `git@github.com:Mortified2896/BlockNations.git`.
- Checkout: `/srv/blocknations`.
- Branch: `main`.
- PM2 app: `blocknations-pbp`.
- Local health: `http://127.0.0.1:8080/healthz`.
- Node.js/npm and PM2 installed on the host.
- A securely provisioned `PBP_SHARED_SECRET` available to the relay process.

A whole-repository checkout remains acceptable for MVP. Keep secrets and persistent PBp data outside Git. Turn/claim storage currently resolves relative to the server checkout under `data/PlayByPost/Turns/`; preserve/back up that directory across host changes.

## First setup

Prepare the directory and install runtime dependencies:

```bash
sudo mkdir -p /srv/blocknations
sudo chown "$USER":"$USER" /srv/blocknations
git clone git@github.com:Mortified2896/BlockNations.git /srv/blocknations
cd /srv/blocknations
npm ci --omit=dev
```

Supply `PBP_SHARED_SECRET` through the host's private process environment or secret management before starting PM2. The server does not load an environment file automatically. Do not put the secret value in the repository or copied commands/output.

```bash
pm2 start npm --name blocknations-pbp -- start
pm2 save
curl --fail --silent --show-error http://127.0.0.1:8080/healthz
```

A passing health route alone is insufficient: protected routes return 401 if the secret is missing. Validate an authenticated client flow using the intended client build.

Before restarting an existing installation, verify its actual process command/revision. A process configured directly for `server.prod.js` is not switched to `server.js` merely by restarting it. The older copy lacks current seat claims/status metadata.

## Normal deployment

```bash
cd /srv/blocknations
./deploy.sh
```

The script checks out the chosen branch, pulls with `--ff-only`, runs `npm ci --omit=dev` only if `package-lock.json` changed, restarts an existing PM2 app with `--update-env` or starts `npm start`, then checks local health.

The script has no automatic rollback, persistent-data backup, compatibility test, or public TLS check. If dependencies are absent despite an unchanged lockfile, install them before invoking it. Run it with the intended securely provisioned process environment.

Overrides:

```bash
APP_DIR=/srv/blocknations \
BRANCH=main \
PM2_NAME=blocknations-pbp \
HEALTH_URL=http://127.0.0.1:8080/healthz \
./deploy.sh
```

## Unity URL and credential resolution

The shared resource is `Assets/Resources/PbpTransportSettings.asset`. Its default URL is currently `https://blocknations.moneymattersmedia.com`. An asset pointing at that URL is configuration, not deployment evidence. Staging is explicitly selected rather than the default.

Current behavior in `HttpTurnTransport` and `MacDevelopmentPbpSecretProvisionPostProcess`:

| Client | Credential behavior |
| --- | --- |
| Editor/general fallback | `PBP_SHARED_SECRET` environment override, then the URL-selected project secret file, then scoped and legacy PlayerPrefs. |
| macOS standalone, development or release | Uses only the provisioned `<app>/Contents/Resources/pbp-api-key.staging` file. Despite the class/file name, this path is not restricted to development builds. |
| iOS/Android non-development release | Prefers nonempty bundled `releaseMobileApiKey`; if absent, continues through the general fallback chain. |

The URL-selected project file is `UserSettings/pbp-api-key.staging` for the designated staging URL (`https://staging.blocknations.moneymattersmedia.com`), otherwise `UserSettings/pbp-api-key.default`. The macOS postprocess chooses that same source based on URL but always uses the historical destination filename `pbp-api-key.staging`.

The tracked transport asset currently contains a populated mobile release credential. This is a distributed MVP playtest mechanism, not confidential participant authentication. Review provisioning/rotation before reopening external playtesting; do not copy its value into new commits, documentation, or reports.

Selecting staging requires the staging URL and matching ignored project secret file. For local testing, use [local setup](Local_Server_Setup.md). Treat changes to release credential provisioning as separate from documentation cleanup.

## Cloudflare hosting direction

For renewed playtesting, retaining the persistent Node relay behind Cloudflare routing/Tunnel is the smallest change to the storage model. Tunnel maps a public hostname to an existing origin service; the Node process and data still need a persistent host. See [Cloudflare Tunnel documentation](https://developers.cloudflare.com/tunnel/).

Moving the relay into Workers requires a separate persistence/concurrency design. Workers' writable filesystem is temporary and scoped to requests, so the current turn/claim files cannot be treated as durable storage there. See [Workers filesystem documentation](https://developers.cloudflare.com/workers/runtime-apis/nodejs/fs/).

Publishing a browser-playable Unity WebGL build is separate from hosting this relay. No WebGL build/acceptance is established by the current restart audit.

## Readiness and acceptance

Resolve the [confirmed concurrent-submit overwrite bug](Development_Followups.md) before renewed online playtesting. Then verify:

- Actual deployed entry point/revision, securely supplied credentials, and persistent data location.
- Public TLS/health and protected access through the selected proxy/tunnel, including client-IP rate limits.
- Fresh supported clients create, claim, submit, fetch, and resume with independent POV/turn/sequence state.
- 2-, 3-, and 4-seat progression and eliminated/resigned-seat skipping.
- App/protocol mismatch handling and continued play across a relay restart.

On 2026-10-07, two unauthenticated HTTPS checks to the configured public endpoint failed during TLS negotiation. The origin's running process, release, and authenticated acceptance remain to be verified.
