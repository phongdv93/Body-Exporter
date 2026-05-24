# bodyexporter.com — FastAPI site

Marketing site + admin CMS + SePay payment page for **SolidWorks Body Exporter**.

License emails are sent by the **Cloudflare Worker** (`/webhook/sepay`) via **Resend** — not by this Python app.

**Push code lên GitHub (từng bước):** xem [`docs/DEPLOY-GITHUB-VI.md`](../docs/DEPLOY-GITHUB-VI.md) trong repo gốc.

## Quick start (local)

```powershell
cd website
.\install.ps1    # first time only
.\run.ps1
```

Venv lives at **`%USERPROFILE%\.venvs\bodyexporter-web`** (not `website\.venv`) so locked `.pyd` files in the repo folder do not block installs.

**Port / file locked?**

```powershell
.\stop-server.ps1
.\install.ps1
```

Delete old broken `website\.venv` only **after** stopping servers (optional):

```powershell
.\stop-server.ps1
Remove-Item -Recurse -Force .venv
```

## Admin login issues

Password is stored in SQLite when the server **first** creates the admin user. Changing `ADMIN_PASSWORD` in `.env` alone does **not** update it.

**Reset password from `.env`:**

```powershell
cd website
# stop uvicorn if running
.\reset-admin.ps1
```

Then log in at `/admin` with `ADMIN_USERNAME` / `ADMIN_PASSWORD` from `.env`.

**Nuclear option** (loses CMS edits in DB): delete `website\data\site.db` and restart — admin is recreated from `.env`.

- Site: http://127.0.0.1:8080  
- Admin: http://127.0.0.1:8080/admin (user/password from `.env`)

## Ngôn ngữ (VI / EN)

- Công khai: **Tiếng Việt | English** trên header → cookie `be_lang` (400 ngày).
- URL: `/lang/en?next=/download` hoặc trình duyệt gửi `Accept-Language: en`.
- Chuỗi UI: `website/app/locales/vi.py`, `en.py`.
- Nội dung marketing / meta / `/buy` / `/download`: **Admin → Chỉnh nội dung** (tiếng Việt + **English**). Site chỉ đọc database; để trống EN → dùng cột tiếng Việt. `/buy`: tiêu đề, intro, ghi chú form/QR, footer, `/buy/success`. `/download`: intro, policy, guides (HTML). Nhãn nút/menu mặc định vẫn từ locale nếu không có HTML tùy chỉnh.

## SEO & Google Search Console

- Every public page: `<title>`, `description`, `keywords`, `canonical`, Open Graph, Twitter card, JSON-LD (`WebSite` + `SoftwareApplication`).
- Defaults: `SEO_DESCRIPTION` + `SEO_KEYWORDS` in `.env` (optional). Short intro on homepage still feeds meta description.
- **`/robots.txt`** — allows `/`, blocks `/admin`, `/api/`, webhooks; points to sitemap.
- **`/sitemap.xml`** — `/`, `/download`, `/buy`, `/terms-and-conditions`, `/privacy`, `/refund`. Không gồm `/buy/success`.

### Paddle (thanh toán quốc tế)

Trên form verify website, dùng:

| Paddle field | URL |
|--------------|-----|
| Pricing page | `https://bodyexporter.com/buy` (giá hiển thị **trước** khi nhập email) |
| Terms of service | `https://bodyexporter.com/terms-and-conditions` |
| Privacy policy | `https://bodyexporter.com/privacy` |
| Refund policy | `https://bodyexporter.com/refund` |

Link ba trang policy cũng có ở **footer** mọi trang công khai. Tùy chọn: `LICENSE_PRICE_USD` / `USD_VND_RATE` trong `.env` để hiện giá USD tham khảo trên `/buy`.

### Thanh toán theo quốc gia (`/buy`)

- IP **Việt Nam** (header `CF-IPCountry` hoặc GeoIP): mặc định **VietQR**; công tắc chuyển sang **Quốc tế (Paddle)**.
- IP **khác**: mặc định **Paddle**; công tắc sang VietQR nếu cần.
- Cookie `be_pay_mode` = `vn` | `intl` (hoặc `?pay=vn` / `?pay=intl`).

**Mã giảm giá VietQR (SePay CK):** không dùng chung Paddle. Tạo trong **Admin → Nội dung** (mỗi dòng `MÃ:phần_trăm`, vd `TESTRUN:99`) hoặc env `VN_DISCOUNT_CODES=TESTRUN:99`. Khách nhập mã trong modal VietQR → QR đổi số tiền; webhook chấp nhận đúng số sau giảm. Tối đa **99%** (100% = 0 VND, không tạo QR).

**Paddle (Render env):**

| Biến | Lấy ở đâu |
|------|-----------|
| `PADDLE_CLIENT_TOKEN` | Paddle → Developer tools → Authentication → Client-side token |
| `PADDLE_API_KEY` | API key (secret) — dùng kiểm tra price + tùy API server |
| `PADDLE_PRICE_ID` | Catalog → Price (`pri_…`) |
| `PADDLE_WEBHOOK_SECRET` | Notifications → destination → endpoint secret key |
| `PADDLE_ENV` | `sandbox` hoặc `production` |

Webhook URL: `https://bodyexporter.com/webhook/paddle` — subscribe ít nhất:
`transaction.completed`, `transaction.paid`, `subscription.created` (bắt buộc nếu gói có **trial**).

**Không nhận email key sau Paddle?** Kiểm tra lần lượt:
1. Paddle → Notifications → destination → log gửi tới `/webhook/paddle` (200, không 401).
2. Render: `PADDLE_WEBHOOK_SECRET` trùng secret destination; `PADDLE_API_KEY` có quyền đọc customer (lấy email từ `customer_id`).
3. Render: `RESEND_API_KEY` + `RESEND_FROM` (domain đã verify Resend).
4. **Admin → Danh sách license**: đã có key cho email chưa? Có key mà không mail = thiếu Resend; gửi lại tay hoặc cấu hình Resend.
5. Gói **14 ngày trial**: license gửi khi bắt đầu trial (`subscription.created`), không đợi đến ngày trừ tiền.  
Sau deploy: **Admin → Dashboard** xem dòng Paddle (price API OK / lỗi). Thử sandbox trên `/buy?pay=intl`.

**Lỗi checkout «Something went wrong» / Network 403 / «Client token is invalid»:**

1. `PADDLE_CLIENT_TOKEN` phải là **Client-side token** (`live_…` khi `PADDLE_ENV=production`), **không** dán API key (`pdl_live_apikey_…`).
2. Paddle → **Developer tools → Authentication** → tạo token mới → copy vào Render → **Redeploy**.
3. Paddle → **Checkout → Checkout settings** → **Default payment link** = `https://bodyexporter.com/buy/paddle` và domain **Approved**.
4. Khách VN: dùng **VietQR** trên `/buy` (Paddle hay bị chặn `checkout-service.paddle.com` từ VN/VPN).

**Checkout hiện «Return to HoaPhong» / tên cá nhân thay vì Body Exporter:** không set trong code website — Paddle lấy **Legal Name** / **Company Display Name** trên tài khoản seller. Vào Paddle Dashboard → **Account / Invoice settings** (hoặc Checkout settings) → đặt **Company Display Name** = `Body Exporter`. Đổi **Legal Name** cần email **sellers@paddle.com**. Đồng thời chỉnh **Statement descriptor** (Checkout → Transactions) cho khớp thẻ ngân hàng khách.
- Optional **`SEO_OG_IMAGE`** in `.env` = full URL of a 1200×630 image for social share.

**Production bắt buộc:** biến `SITE_URL=https://bodyexporter.com` (không dấu `/` cuối). Nếu sai, sitemap sẽ ghi URL `127.0.0.1` → Google không index.

### Kiểm tra sau deploy

```text
https://bodyexporter.com/robots.txt    → dòng Sitemap: https://bodyexporter.com/sitemap.xml
https://bodyexporter.com/sitemap.xml   → 3 URL https://bodyexporter.com/...
```

### Đưa lên Search Console (lần đầu)

1. [Google Search Console](https://search.google.com/search-console) → **Thêm thuộc tính** → URL tiền tố: `https://bodyexporter.com` (hoặc domain `bodyexporter.com`).
2. **Xác minh** quyền sở hữu (DNS TXT trên Cloudflare, hoặc file HTML).
3. Menu **Sơ đồ web** (Sitemaps) → nhập: `sitemap.xml` → **Gửi** (URL đầy đủ: `https://bodyexporter.com/sitemap.xml`).
4. **Kiểm tra URL** → thử `https://bodyexporter.com/` và `/download` → **Yêu cầu lập chỉ mục** (Request indexing) cho từng URL quan trọng.
5. Đợi vài ngày–2 tuần; trang mới thường không lên ngay.

Render/Railway: sau khi đổi `SITE_URL`, redeploy và mở lại sitemap trên trình duyệt để chắc domain đúng.

## Nhận mail `hotro@bodyexporter.com` (Cloudflare)

1. Domain **bodyexporter.com** phải dùng **DNS Cloudflare** (nameserver Cloudflare).
2. Dashboard Cloudflare → **Email** → **Email Routing** → **Enable**.
3. **Destination addresses**: thêm Gmail thật của bạn (verify email).
4. **Routing rules** → **Create address** `hotro@bodyexporter.com` → forward tới Gmail.
5. Cloudflare tự thêm bản ghi **MX** (và **TXT** SPF cho routing) — **không xóa**.

Mail **gửi** license tự động: **Resend** + Worker (`noreply@bodyexporter.com`), không qua Python site — làm tiếp:

### Resend (gửi mail license — Render + VietQR/Paddle webhook)

**Hình 1** (mail đơn giản) = website gửi qua Resend API. **Hình 2** (`help@paddle.com`) = mail hóa đơn của Paddle — không chứa license key; tắt trong Paddle nếu không cần. **Hình 3** = template Resend — gắn vào site:

1. Resend → Templates → **Publish** template (không để Draft).
2. Copy **Template ID** (hoặc alias).
3. Render env (hai template **Published**):
   - `RESEND_LICENSE_TEMPLATE_ID_VI` — HTML tiếng Việt (`template.html` của mày)
   - `RESEND_LICENSE_TEMPLATE_ID_EN` — HTML tiếng Anh (`website/email-templates/license-en.html`)
   - Biến: `name`, `license_key`, `plan`, `expires`
4. Redeploy → **VietQR** gửi mail VI, **Paddle** gửi mail EN.

Không set template id → mail plain (VI hoặc EN tùy kênh thanh toán).

### Resend (Worker — tùy chọn)

1. [resend.com](https://resend.com) → **Domains** → Add **bodyexporter.com**.
2. Thêm bản ghi DNS (DKIM, SPF) trong Cloudflare — Resend hiển thị đúng giá trị.
3. Đợi domain **Verified**.
4. Trên Worker (repo này):

```powershell
cd server
wrangler secret put RESEND_API_KEY
wrangler secret put RESEND_FROM
# Body Exporter <noreply@bodyexporter.com>
wrangler deploy
```

5. Cập nhật `drapf` / `Push-ClientConfig.ps1`:

```powershell
supportEmail = "hotro@bodyexporter.com"
supportUrl = "https://bodyexporter.com"
paymentWebUrl = "https://bodyexporter.com/buy"
```

### Biến môi trường site (`.env` production)

```
SUPPORT_EMAIL=hotro@bodyexporter.com
SITE_URL=https://bodyexporter.com
```

## Link Google Drive — tải thẳng, không mở trang Drive

1. Upload file `.zip` → **Share** → **Anyone with the link** (Viewer).
2. Copy link dạng:  
   `https://drive.google.com/file/d/FILE_ID_HERE/view?usp=sharing`
3. **URL tải trực tiếp** (dán vào Admin → URL file ZIP):  

   `https://drive.google.com/uc?export=download&id=FILE_ID_HERE`

4. File **rất lớn** (>~100MB) Google đôi khi trả trang “virus scan” thay vì tải ngay. Cách ổn định hơn:
   - **GitHub Releases** (asset link tải thẳng), hoặc  
   - **Cloudflare R2** / bucket S3 public URL.

## Deploy production

FastAPI **không** host trên Cloudflare Workers. Chọn một:

### A) Fly.io (Docker, gần VN: `sin`)

```powershell
cd website
fly auth login
fly launch --copy-config   # uses fly.toml; set unique app name if taken
fly secrets set SECRET_KEY="..." ADMIN_USERNAME=admin ADMIN_PASSWORD="..." SITE_URL="https://bodyexporter.com"
fly deploy
```

Gắn domain: Fly dashboard → **Certificates** → add `bodyexporter.com`. Trên Cloudflare DNS: **CNAME** `www` → `your-app.fly.dev`, hoặc bật **proxy** tùy hướng dẫn Fly.

**SQLite**: tạo volume Fly gắn `/app/data` nếu không muốn mất DB mỗi lần deploy — xem [Fly volumes](https://fly.io/docs/reference/volumes/).

### B) Railway / Render

- Repo chứa thư mục `website/` — **Railway**: *Không* cần đổi Root Directory nếu đã có **`Dockerfile` ở root repo** (monorepo) — file đó copy từ `website/` và chạy uvicorn.
- **Biến môi trường** (Variables): `PORT` do Railway tự set; bạn thêm `SECRET_KEY`, `SITE_URL`, `DATABASE_URL`, `ADMIN_PASSWORD`, v.v. (xem `website/.env.example`).
- Nếu bạn xóa Dockerfile root và chỉ build từ `website/`: trong Railway → Service → **Root Directory** = `website`.
- **Start command** trên Railway để trống khi dùng Docker (xem `railway.toml`).

### C) Cloudflare Tunnel → máy bạn / VPS

1. Cài [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/).
2. Chạy site: `.\run.ps1` hoặc Docker.
3. Tạo Tunnel trong Cloudflare Zero Trust, **Public hostname** `bodyexporter.com` → `http://127.0.0.1:8080`.
4. DNS `bodyexporter.com` theo Tunnel (CNAME ghi trong dashboard).

**Mình không thể deploy giúp từ đây** — cần tài khoản Fly/Railway/Cloudflare của bạn (`fly auth login`, v.v.). Làm theo các bước trên trên máy bạn.

### PostgreSQL (giống flow nesting.click: code trên GitHub, DB managed)

- **Code** push lên GitHub; **server** (Railway / Fly / Render…) build từ repo hoặc Docker image.
- Tạo DB: **Neon**, **Supabase**, **Railway Postgres**, **Fly Postgres**, v.v. → copy **connection string**.
- Set biến môi trường **`DATABASE_URL`** (hoặc dán vào `.env` production):

```env
DATABASE_URL=postgresql://user:pass@ep-xxx.region.aws.neon.tech/neondb?sslmode=require
```

- App tự thêm driver `psycopg2`; nếu host đưa `postgres://` cũng được.
- **Không set** `DATABASE_URL` → dev local vẫn dùng **SQLite** `data/site.db`.
- Lần chạy đầu: `init_db()` gọi `create_all` + seed `be_site_content` + admin (nếu bảng trống). Bản cũ tự đổi tên `site_content` → `be_site_content`, v.v. **chỉ khi** cột bảng khớp Body Exporter (tránh đụng bảng `licenses` của app khác khi dùng chung Postgres).
- Nếu log báo `column be_licenses.buyer_email does not exist`: bảng `be_licenses` sai schema (thường do gộp Postgres với app khác). Bản mới **tự DROP + tạo lại** `be_licenses` khi startup nếu thiếu cột. Deploy lại rồi mở `/admin/licenses` — hoặc tay: `DROP TABLE IF EXISTS be_licenses;` rồi restart service.

**Lưu ý:** Đừng commit file `.env` có thật `DATABASE_URL` lên GitHub — chỉ lưu trong secrets của Railway/Fly/GitHub Actions.

## Deploy on Cloudflare (lưu ý)

FastAPI không chạy trên **Workers** thuần. Chỉ **Tunnel** hoặc trỏ domain tới **Fly/Railway/VPS**.

Trường hợp bạn muốn **chỉ static**: có thể build export HTML — **không** áp dụng cho site admin hiện tại.

## SePay

### Bank QR (works today)

Admin → **URL gốc QR Sepay** — same as `paymentVnSepayUrl` in Worker client-config.

Customer flow: `/buy` → enter email → QR with memo `BE email@...` → Worker webhook mints license → Resend email.

### Payment Gateway (card, optional)

In `.env`:

```
SEPAY_PG_MERCHANT_ID=...
SEPAY_PG_SECRET_KEY=...
SEPAY_PG_ENV=sandbox
```

IPN for PG should still point to your Worker (configure in my.sepay.vn). Card payments need PG IPN handler on Worker (future) — QR path is fully wired.

## Admin

- `/admin` — dashboard  
- `/admin/content` — hero, download ZIP URL, prices, QR base URL  
- `/admin/licenses` — danh sách license (Postgres), tạo tay

Change `ADMIN_PASSWORD` in production; default is created on first boot from `.env`.

## Plugin download URL

Upload `BodyExporter-vX.zip` → **direct URL** (Google Drive `uc?export=download&id=...`, GitHub Release, R2) → paste in **Admin → URL file ZIP**.

Then run `Publish-UpdateManifest.ps1` for in-app update checks.
