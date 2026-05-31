#!/usr/bin/env bash
# Run on VPS after rsync (GitHub Actions or manual SSH).
# App: /var/www/bodyexporter.com/website — systemd bodyexporter — port 8002
set -euo pipefail

APP_DIR="/var/www/bodyexporter.com/website"
SERVICE="bodyexporter"
PORT="8002"

cd "$APP_DIR"

if [ ! -f .env ]; then
  echo "ERROR: $APP_DIR/.env missing (create once on server, never commit)."
  exit 1
fi

if [ ! -d .venv ]; then
  python3 -m venv .venv
fi

# shellcheck disable=SC1091
. .venv/bin/activate
pip install --upgrade pip
pip install -r requirements.txt

sudo systemctl restart "$SERVICE"
sleep 2
sudo systemctl is-active --quiet "$SERVICE"
curl -fsS "http://127.0.0.1:${PORT}/robots.txt" | head -n 1
echo "Deploy OK: $SERVICE on port $PORT"
