# Auto deploy bodyexporter.com (GitHub Actions → VPS)

Giống **nesting.click**: push `main` (có đổi `website/**`) → GitHub rsync lên VPS → `pip install` → `systemctl restart bodyexporter`.

## Một lần trên VPS (bạn đã làm thủ công — chỉ kiểm tra)

| Mục | Giá trị |
|-----|---------|
| App dir | `/var/www/bodyexporter.com/website` |
| Service | `bodyexporter` |
| Port | `8002` (uvicorn bind `127.0.0.1`) |
| Env | `/var/www/bodyexporter.com/website/.env` (**không** commit git) |
| venv | `/var/www/bodyexporter.com/website/.venv` |

Deploy user (SSH) cần quyền:

```bash
# Cho user deploy (vd. deploy) restart service không hỏi mật khẩu:
echo 'deploy ALL=(ALL) NOPASSWD: /bin/systemctl restart bodyexporter, /bin/systemctl is-active bodyexporter' | sudo tee /etc/sudoers.d/deploy-bodyexporter
sudo chmod 440 /etc/sudoers.d/deploy-bodyexporter
```

Template systemd: [`website/deploy/bodyexporter.service`](../website/deploy/bodyexporter.service).

Nginx/Caddy proxy `bodyexporter.com` → `127.0.0.1:8002` (giữ như cấu hình hiện tại).

## GitHub Secrets (repo SolidWorks Body Exporter)

**Settings → Secrets and variables → Actions → New repository secret**

Dùng **cùng bộ secret nesting.click** nếu cùng VPS:

| Secret | Ví dụ |
|--------|--------|
| `DEPLOY_HOST` | IP hoặc hostname VPS |
| `DEPLOY_USER` | `deploy` hoặc user SSH bạn đang dùng |
| `DEPLOY_SSH_KEY` | Private key **đầy đủ** (BEGIN…END), user đã có trong `authorized_keys` |
| `DEPLOY_SSH_PORT` | (tuỳ chọn) `22` |

Tạo key riêng cho Actions (khuyên dùng):

```bash
ssh-keygen -t ed25519 -C "github-actions-bodyexporter" -f ~/.ssh/gh_bodyexporter -N ""
cat ~/.ssh/gh_bodyexporter.pub   # dán vào VPS ~/.ssh/authorized_keys
cat ~/.ssh/gh_bodyexporter       # dán vào secret DEPLOY_SSH_KEY
```

## Bật workflow

1. Commit file [`.github/workflows/deploy-bodyexporter.yml`](../.github/workflows/deploy-bodyexporter.yml) lên `main`.
2. **Actions** → **Deploy bodyexporter.com** → **Run workflow** (test tay lần đầu).
3. Sau đó: sửa code trong `website/` → commit → push → deploy tự chạy.

Workflow **không** ghi đè `.env`, `data/`, `uploads/` trên server.

## Sửa plugin / server Worker

Chỉ đổi `website/**` mới trigger deploy. Đổi `src/` hoặc `server/` **không** deploy web (đúng ý).

## Lỗi thường gặp

| Lỗi | Cách xử lý |
|-----|------------|
| Missing secrets | Thêm 3 secret trên GitHub |
| `.env missing on server` | Tạo `.env` production trên VPS (copy từ Render / `.env.example`) |
| `sudo: a password is required` | Thêm sudoers như trên |
| `curl 8002` fail | `journalctl -u bodyexporter -n 50` trên VPS |
| Permission denied rsync | User SSH phải ghi được `APP_DIR` (thường thuộc group `www-data`) |
