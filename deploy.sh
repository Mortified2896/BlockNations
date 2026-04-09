#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR:-/srv/blocknations}"
BRANCH="${BRANCH:-main}"
PM2_NAME="${PM2_NAME:-blocknations-pbp}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/healthz}"

cd "$APP_DIR"

OLD_HEAD="$(git rev-parse HEAD)"

git checkout "$BRANCH"
git pull --ff-only origin "$BRANCH"

if ! git diff --quiet "$OLD_HEAD" HEAD -- package-lock.json; then
  npm ci --omit=dev
fi

if pm2 describe "$PM2_NAME" >/dev/null 2>&1; then
  pm2 restart "$PM2_NAME" --update-env
else
  pm2 start npm --name "$PM2_NAME" -- start
fi

sleep 2
curl --fail --silent --show-error "$HEALTH_URL"
