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

restart_service() {
  # Never use bare "sudo" — GitHub Actions has no TTY/password → intermittent failures.
  if sudo -n systemctl restart "$SERVICE" 2>/dev/null; then
    return 0
  fi
  if systemctl restart "$SERVICE" 2>/dev/null; then
    return 0
  fi
  echo ""
  echo "ERROR: Cannot restart $SERVICE without a password."
  echo "GitHub Actions deploy user: $(whoami)"
  echo ""
  echo "Fix once on VPS (SSH as root):"
  echo "  sudo bash /var/www/bodyexporter.com/deploy/setup-deploy-sudo.sh $(whoami)"
  echo ""
  echo "Then verify:"
  echo "  sudo -n systemctl is-active $SERVICE"
  echo ""
  exit 1
}

is_service_active() {
  if sudo -n systemctl is-active --quiet "$SERVICE" 2>/dev/null; then
    return 0
  fi
  systemctl is-active --quiet "$SERVICE" 2>/dev/null
}

restart_service
sleep 2
if ! is_service_active; then
  echo "ERROR: $SERVICE is not active after restart."
  sudo -n systemctl status "$SERVICE" --no-pager 2>/dev/null || systemctl status "$SERVICE" --no-pager || true
  exit 1
fi

curl -fsS "http://127.0.0.1:${PORT}/robots.txt" | head -n 1
echo "Deploy OK: $SERVICE on port $PORT"
