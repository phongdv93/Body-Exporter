#!/usr/bin/env bash
# Run on VPS after rsync (GitHub Actions or manual SSH).
# App: /var/www/bodyexporter.com/website — systemd bodyexporter — port 8002
set -euo pipefail

APP_DIR="/var/www/bodyexporter.com/website"
SERVICE="bodyexporter"
PORT="8002"
READY_TRIES="${READY_TRIES:-45}"

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

# Ensure upload dir exists for www-data (rsync excludes uploads/).
mkdir -p "$APP_DIR/uploads/blog" || true
if command -v sudo >/dev/null 2>&1; then
  sudo -n chown -R www-data:www-data "$APP_DIR/uploads" 2>/dev/null || true
  sudo -n chmod -R u+rwX,g+rwX "$APP_DIR/uploads" 2>/dev/null || true
fi

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

dump_service_logs() {
  echo "---- systemctl status ----"
  sudo -n systemctl status "$SERVICE" --no-pager -l 2>/dev/null \
    || systemctl status "$SERVICE" --no-pager -l 2>/dev/null \
    || true
  echo "---- journalctl (last 80) ----"
  sudo -n journalctl -u "$SERVICE" -n 80 --no-pager 2>/dev/null \
    || journalctl -u "$SERVICE" -n 80 --no-pager 2>/dev/null \
    || true
}

wait_for_http() {
  local url="$1"
  local i
  for i in $(seq 1 "$READY_TRIES"); do
    if curl -fsS --max-time 2 "$url" >/dev/null 2>&1; then
      echo "Ready after ${i}s: $url"
      return 0
    fi
    if ! is_service_active; then
      echo "ERROR: $SERVICE became inactive while waiting for $url"
      dump_service_logs
      return 1
    fi
    sleep 1
  done
  echo "ERROR: timed out after ${READY_TRIES}s waiting for $url"
  dump_service_logs
  return 1
}

restart_service
sleep 1
if ! is_service_active; then
  echo "ERROR: $SERVICE is not active after restart."
  dump_service_logs
  exit 1
fi

# Prefer /health (fast JSON). Fall back to /robots.txt.
if ! wait_for_http "http://127.0.0.1:${PORT}/health"; then
  wait_for_http "http://127.0.0.1:${PORT}/robots.txt" || exit 1
fi

curl -fsS "http://127.0.0.1:${PORT}/robots.txt" | head -n 1
echo "Deploy OK: $SERVICE on port $PORT"
