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
