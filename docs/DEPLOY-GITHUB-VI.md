# Đưa code lên GitHub — từng bước

**“Deploy lên GitHub repo”** = lưu source code trên GitHub.  
Site **FastAPI + PostgreSQL** vẫn **chạy trên Railway / Fly / VPS** (hoặc Tunnel); GitHub chỉ host **git**, không chạy Python.

---

## Bước 1 — Tạo repo trên GitHub

1. Đăng nhập [github.com](https://github.com).
2. **+** → **New repository**.
3. **Repository name** (ví dụ): `solidworks-body-exporter` hoặc gộp với monorepo hiện có.
4. Chọn **Private** (nếu có plugin / logic nhạy cảm).
5. **Không** tích “Add README” nếu máy bạn đã có code (tránh lệch lịch sử).
6. **Create repository**.

Giữ lại URL repo, dạng:
`https://github.com/TEN_USER/TEN_REPO.git`

---

## Bước 2 — Git trên máy (PowerShell)

Mở PowerShell:

```powershell
cd "D:\MyPython\My Solidworks Plugin"
```

Nếu **chưa** có thư mục `.git`:

```powershell
git init
```

---

## Bước 3 — Kiểm tra sẽ không commit nhầm secret

Đảm bảo **không** có:

- `website\.env` (đã trong `.gitignore`)
- `drapf` (script có token — đã ignore)
- File zip chứa license key tùy ý

```powershell
git status
```

---

## Bước 4 — Thêm remote GitHub

Thay `TEN_USER` / `TEN_REPO`:

```powershell
git remote add origin https://github.com/TEN_USER/TEN_REPO.git
```

(Nếu đã có `origin` sai: `git remote remove origin` rồi `add` lại.)

---

## Bước 5 — Commit đầu tiên

User rule: chỉ tạo commit khi bạn **chủ động** yêu cầu. Làm tay:

```powershell
git add -A
git status
git commit -m "Initial commit: SolidWorks Body Exporter + website + server"
```

Nếu Git hỏi name/email lần đầu:

```powershell
git config user.email "you@example.com"
git config user.name "Your Name"
```

(Chỉ trong repo này — không dùng `--global` trừ khi bạn muốn.)

---

## Bước 6 — Push lên GitHub

Nhánh mặc định thường là `main`:

```powershell
git branch -M main
git push -u origin main
```

Nếu GitHub yêu cầu đăng nhập: dùng **Personal Access Token** (Settings → Developer settings) thay mật khẩu HTTPS, hoặc cài **GitHub CLI** (`gh auth login`).

---

## Bước 7 — CI (tuỳ chọn)

Đã có workflow [`.github/workflows/website.yml`](../.github/workflows/website.yml): mỗi push vào nhánh có thay đổi `website/**` sẽ chạy `pip install` + import app trên GitHub Actions.

---

## Bước 8 — Chạy site thật (không phải GitHub)

1. **PostgreSQL** (Neon / Supabase / Railway / …) → `DATABASE_URL`.
2. **Railway / Fly / Render**: connect repo GitHub → root directory `website` (nếu platform hỏi).
3. Thêm biến môi trường: `SECRET_KEY`, `ADMIN_PASSWORD`, `SITE_URL`, `DATABASE_URL`, v.v. (xem `website/.env.example`).
4. Deploy; mở URL host cấp hoặc domain `bodyexporter.com`.

Chi tiết: [`website/README.md`](../website/README.md).

---

## Lỗi thường gặp

| Lỗi | Cách xử lý |
|-----|------------|
| `remote origin already exists` | `git remote -v` → `git remote set-url origin ...` |
| `failed to push ... non-fast-forward` | Repo GitHub có README/commit sẵn → `git pull origin main --rebase` rồi `push` lại |
| Đẩy nhầm `.env` | **Đổi toàn bộ secret** trên server; xóa file khỏi git history (`git filter-repo` / hỗ trợ GitHub) nếu đã public |

---

Sau khi push xong, gửi mình **URL repo** (hoặc screenshot lỗi `git push`) nếu cần chỉnh thêm (branch, submodule, monorepo).
