# Auto deploy bodyexporter.com (GitHub Actions → VPS)

Giống **nesting.click**: push `main` (đổi `website/**`) → rsync VPS → chạy `deploy/deploy-bodyexporter.sh` → restart `bodyexporter`.

## Files trong repo

| File | Việc |
|------|------|
| [`.github/workflows/deploy-vps.yml`](../.github/workflows/deploy-vps.yml) | GitHub Actions |
| [`deploy/deploy-bodyexporter.sh`](../deploy/deploy-bodyexporter.sh) | Script trên VPS (pip + restart) |

## Một lần trên VPS (bạn đã deploy thủ công — kiểm tra lại)

| Mục | Giá trị |
|-----|---------|
| App dir | `/var/www/bodyexporter.com/website` |
| Deploy script | `/var/www/bodyexporter.com/deploy/deploy-bodyexporter.sh` (Actions tự copy) |
| Service | `bodyexporter` |
| Port | `8002` |
| Env | `/var/www/bodyexporter.com/website/.env` |

User SSH (secret `VPS_USER`) cần **passwordless** `systemctl` — **một lần**, SSH root:

```bash
# Thay deploy bằng đúng VPS_USER
sudo bash /var/www/bodyexporter.com/deploy/setup-deploy-sudo.sh deploy
sudo -u deploy sudo -n systemctl is-active bodyexporter   # → active
```

Hoặc copy script từ repo rồi chạy trước khi push lần đầu. Thiếu bước này → GitHub Actions báo `sudo: a password is required` (không có TTY để gõ pass).

Systemd template: [`website/deploy/bodyexporter.service`](../website/deploy/bodyexporter.service).

## GitHub Secrets (repo Body-Exporter)

**Settings → Secrets and variables → Actions**

| Secret | Mô tả |
|--------|--------|
| `VPS_HOST` | IP / hostname VPS |
| `VPS_USER` | User SSH (vd. `deploy`) |
| `VPS_SSH_KEY` | Private key đầy đủ (BEGIN…END) |
| `VPS_SSH_PORT` | (tuỳ chọn) mặc định `22` |

Cùng VPS với nesting.click → **cùng 3 secret** nếu đã cấu hình ở repo nesting.

## Bật auto deploy

1. Commit `deploy-vps.yml` + `deploy/deploy-bodyexporter.sh` lên `main`.
2. Actions → **Deploy VPS** → **Run workflow** (test).
3. Sau đó: sửa `website/` → commit → push → tự deploy.

Deploy **không** ghi đè `.env`, `data/`, `uploads/`.

## Deploy tay (không qua GitHub)

```bash
# Từ máy dev (đã có SSH):
rsync -az website/ user@vps:/var/www/bodyexporter.com/website/ \
  --exclude .venv --exclude .env --exclude data --exclude uploads
scp deploy/deploy-bodyexporter.sh user@vps:/var/www/bodyexporter.com/deploy/
ssh user@vps 'chmod +x /var/www/bodyexporter.com/deploy/deploy-bodyexporter.sh && /var/www/bodyexporter.com/deploy/deploy-bodyexporter.sh'
```

## Lỗi thường gặp

| Lỗi | Xử lý |
|-----|--------|
| Missing VPS_* secrets | Thêm secrets trên GitHub |
| `.env missing` | Tạo `.env` trên VPS |
| `sudo: a password is required` | Chạy `setup-deploy-sudo.sh` như trên; workflow có preflight |
| Port 8002 fail | `journalctl -u bodyexporter -n 50` |
