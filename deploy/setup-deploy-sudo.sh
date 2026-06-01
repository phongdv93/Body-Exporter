#!/usr/bin/env bash
# One-time on VPS (run as root). Enables passwordless systemctl for the GitHub Actions SSH user.
# Usage:
#   sudo bash /var/www/bodyexporter.com/deploy/setup-deploy-sudo.sh deployuser
set -euo pipefail

DEPLOY_USER="${1:-}"
if [ -z "$DEPLOY_USER" ]; then
  echo "Usage: sudo bash setup-deploy-sudo.sh <ssh_deploy_username>"
  exit 1
fi

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo bash $0 $DEPLOY_USER"
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE="${SCRIPT_DIR}/bodyexporter-systemctl.sudoers"
if [ ! -f "$TEMPLATE" ]; then
  echo "Missing $TEMPLATE"
  exit 1
fi

DEST="/etc/sudoers.d/bodyexporter-deploy"
sed "s/DEPLOY_USER/${DEPLOY_USER}/g" "$TEMPLATE" > "$DEST"
chmod 440 "$DEST"
visudo -c

echo "OK: $DEPLOY_USER can run systemctl for bodyexporter without a password."
echo "Test (as $DEPLOY_USER): sudo -n systemctl is-active bodyexporter"
