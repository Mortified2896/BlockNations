Block Nations VPS Deploy (MVP)

Scope:
- Whole repo clone on the VPS is acceptable for MVP.
- This deploy flow only supports the current root-level Node PBp server.
- Runtime server behavior is unchanged by this patch.

Assumptions:
- Repo path on VPS: `/srv/blocknations`
- Deploy branch: `main`
- PM2 app name: `blocknations-pbp`
- Health URL: `http://127.0.0.1:8080/healthz`

All four can be overridden when running the script:

```bash
APP_DIR=/srv/blocknations \
BRANCH=main \
PM2_NAME=blocknations-pbp \
HEALTH_URL=http://127.0.0.1:8080/healthz \
./deploy.sh
```

First-time VPS setup:

```bash
sudo mkdir -p /srv/blocknations
sudo chown "$USER":"$USER" /srv/blocknations
git clone git@github.com:<your-org-or-user>/BlockNations.git /srv/blocknations
cd /srv/blocknations
npm ci --omit=dev
pm2 start npm --name blocknations-pbp -- start
pm2 save
```

Normal deploy:

```bash
cd /srv/blocknations
./deploy.sh
```

What the script does:
- `git pull --ff-only origin <branch>`
- runs `npm ci --omit=dev` only if `package-lock.json` changed
- `pm2 restart` if the app exists, otherwise first-time `pm2 start`
- checks `/healthz` at the end

Notes:
- Keep live PBp data and secrets managed on the VPS.
- Do not store `PBP_SHARED_SECRET` in Git.

Unity PBp environment/auth behavior:
- Default project behavior is live PBp. `Assets/Resources/PbpTransportSettings.asset` should point `playByPostBaseUrl` at the live server, and public mobile release builds are expected to use live.
- Editor / normal dev path: `PBP_SHARED_SECRET` is the highest explicit override. Otherwise the project secret file is selected from the configured base URL: staging URL uses `UserSettings/pbp-api-key.staging`; live/default URL uses `UserSettings/pbp-api-key.default`. Scoped and legacy PlayerPrefs remain lower-priority local overrides.
- macOS standalone `DEVELOPMENT_BUILD`: uses the provisioned in-app `pbp-api-key.staging` file path. This path is unchanged and separate from normal Editor behavior.
- Non-development iOS release: uses bundled `releaseMobileApiKey` from `PbpTransportSettings.asset`.
- Non-development Android release: uses bundled `releaseMobileApiKey` from `PbpTransportSettings.asset`.
- `releaseMobileApiKey` is an MVP workaround for release testing convenience, not real secret security.
- Staging is not the default. To use staging again later, set the shared PBp base URL to the staging server and make sure `UserSettings/pbp-api-key.staging` exists with the staging secret.
- Release validation reminder: confirm the menu shows `PBp server: Live`, then verify clean-device create/join/submit on fresh installs.
