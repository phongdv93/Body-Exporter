# SETUP-SERVER.md

Hướng dẫn deploy licensing API cho SolidWorks Body Exporter lên Cloudflare Workers
(free tier — không cần thẻ tín dụng, đủ cho ~100k requests/ngày).

## 1. Cài Wrangler (Cloudflare CLI)

Mở PowerShell trên máy của mày:

```powershell
# Node 18+ phải có sẵn. Check:
node --version

# Cài wrangler globally:
npm install -g wrangler
wrangler --version
```

## 2. Login Cloudflare

```powershell
wrangler login
```

Browser sẽ mở → login bằng Cloudflare account. Nếu chưa có account → đăng ký free
tại https://dash.cloudflare.com/sign-up. **Không cần verify thẻ tín dụng** cho
Workers free tier.

## 3. Tạo KV namespaces

KV = key-value database của Cloudflare, free tier cho 100k reads/ngày. Mình
dùng 2 namespace:
- `LICENSE_DB` — chứa toàn bộ license records (1 record / key)
- `LICENSE_BY_MACHINE` — reverse lookup từ machineId → key

```powershell
cd "D:\MyPython\My Solidworks Plugin\server"
npm install                              # cài wrangler + types

wrangler kv:namespace create LICENSE_DB
# -> Output: id = "abc123..."   <- COPY giá trị này

wrangler kv:namespace create LICENSE_BY_MACHINE
# -> Output: id = "def456..."   <- COPY giá trị này
```

Mở `wrangler.toml`, paste 2 ID vào chỗ `REPLACE_WITH_ID_FROM_WRANGLER_KV_NAMESPACE_CREATE`.

## 4. Sinh RSA key pair cho JWT

Server ký JWT bằng private key, client validate bằng public key. Sinh 1 lần:

```powershell
# Cần OpenSSL. Git-for-Windows có sẵn:
& "C:\Program Files\Git\usr\bin\openssl.exe" genrsa -out jwt-private.pem 2048
& "C:\Program Files\Git\usr\bin\openssl.exe" rsa -in jwt-private.pem -pubout -out jwt-public.pem
```

**Hai file vừa sinh:**
- `jwt-private.pem` → KHÔNG bao giờ commit, chỉ paste vào Cloudflare secret (bước 5).
  Backup vào password manager (1Password / Bitwarden) — mất key này là mất hết
  license đã issue.
- `jwt-public.pem` → embed vào addin DLL (xem `Services/Api/LicensePublicKey.cs`).
  Có thể commit, không phải secret.

## 5. Set secrets

```powershell
# ADMIN_TOKEN: bearer token mày dùng để gọi /admin/license/issue. Sinh ngẫu nhiên:
$adminToken = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 48 | % {[char]$_})
$adminToken                              # COPY lại, lưu vào password manager
$adminToken | wrangler secret put ADMIN_TOKEN

# JWT_PRIVATE_KEY: paste toàn bộ nội dung file jwt-private.pem (multiline OK):
Get-Content jwt-private.pem -Raw | wrangler secret put JWT_PRIVATE_KEY
```

## 6. Deploy

```powershell
wrangler deploy
```

Output sẽ in URL dạng `https://bodyexporter-api.<your-subdomain>.workers.dev`.
Đây là `ApiBaseUrl` mày paste vào `%APPDATA%\SolidWorksBodyExporter\settings.json`
trên máy client (xem `ARCHITECTURE.md`).

Test ngay:

```powershell
curl https://bodyexporter-api.<your-subdomain>.workers.dev/health
# -> {"status":"ok","version":"1.0.0"}
```

## 7. Issue license đầu tiên (để test)

```powershell
$adminToken = "<paste ADMIN_TOKEN từ bước 5>"
$apiUrl = "https://bodyexporter-api.<your-subdomain>.workers.dev"

$body = @{ owner = "Test User"; plan = "personal"; days = 365 } | ConvertTo-Json
$response = Invoke-RestMethod -Uri "$apiUrl/admin/license/issue" `
    -Method POST `
    -Headers @{ Authorization = "Bearer $adminToken" } `
    -ContentType "application/json" `
    -Body $body
$response.key   # <- license key, gửi cho khách
```

## 8. Bind license vào billing webhook (Stripe / Lemon Squeezy / etc.)

Workflow:
1. Khách mua qua Stripe → Stripe gửi webhook `payment_intent.succeeded` về endpoint của mày.
2. Endpoint mày gọi `POST /admin/license/issue` để mint key.
3. Endpoint gửi email cho khách kèm key.

Ví dụ webhook handler bằng Node:

```javascript
// Trong server webhook của mày (ví dụ Express + Stripe):
app.post("/webhook/stripe", async (req, res) => {
    const event = req.body;
    if (event.type === "payment_intent.succeeded") {
        const customer = event.data.object.customer_details;
        const r = await fetch("https://bodyexporter-api.<sub>.workers.dev/admin/license/issue", {
            method: "POST",
            headers: {
                Authorization: `Bearer ${process.env.BODYEXPORTER_ADMIN_TOKEN}`,
                "Content-Type": "application/json",
            },
            body: JSON.stringify({ owner: customer.email, plan: "personal", days: 365 }),
        });
        const { key } = await r.json();
        await sendEmail(customer.email, `Your Body Exporter license: ${key}`);
    }
    res.sendStatus(200);
});
```

## 8b. Cấu hình client (tên tác giả, email, Sepay, Lemon Squeezy, cập nhật)

App gọi `GET /v1/client-config` (public). Dữ liệu lưu KV key `__client_config__`. Ghi đè bằng `PUT /admin/client-config` (Bearer `ADMIN_TOKEN`):

```powershell
$cfg = @{
  authorName = "Gió"
  supportEmail = "hotro@example.com"
  supportUrl = "https://example.com/support"
  latestVersion = "0.7.2"
  updateManifestUrl = "https://example.com/bodyexporter/update-manifest.json"
  paymentVnTitle = "Chuyển khoản / Sepay"
  paymentVnBody = "Nội dung CK: BE + email. Sau khi nhận tiền gửi license .lic."
  paymentVnSepayUrl = "https://qr.sepay.vn/..."
  paymentIntlTitle = "Lemon Squeezy (international)"
  paymentIntlBody = "International checkout via Lemon Squeezy (card, PayPal where available)."
  paymentIntlLemonsqueezyUrl = "https://YOURSTORE.lemonsqueezy.com/checkout/buy/YOUR_VARIANT_ID"
  paymentIntlTripleUrl = ""
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "$apiUrl/admin/client-config" -Method PUT `
  -Headers @{ Authorization = "Bearer $adminToken"; "Content-Type" = "application/json" } -Body $cfg
```

Manifest cập nhật (JSON tại `updateManifestUrl`): `{ "version": "0.7.3", "downloadUrl": "https://...", "sha256": "...", "releaseNotes": "..." }`. Nút **Check for updates** trong cửa sổ License: nếu `version` trong manifest **không lớn hơn** bản add-in đang chạy thì app báo *You are running the latest version*; nếu có bản mới hơn thì hỏi có mở `downloadUrl` hay không. Không tự cài im lặng (nên dùng installer ký Authenticode).

## 8c. Luồng Buy license trong add-in (thanh toán + server)

1. **Nội dung hiển thị**: `GET /v1/client-config` trả về `paymentVnBody`, `paymentVnSepayUrl`, `paymentIntlBody`, `paymentIntlLemonsqueezyUrl`. User bấm **Activate / help** → bung phần kích hoạt + **Thanh toán Việt Nam** (QR Sepay inline) / **International (Lemon)**.
2. **Sau khi khách trả tiền**: Lemon + SePay webhook tự mint license và gửi email (mục 8d, 8e). Khách dán UUID vào ô key hoặc dùng file `.lic` nếu bạn gửi offline.
3. **`updateManifestUrl`**: cần có trong client-config nếu muốn nút **Check for updates** hoạt động.

## 8d. Lemon Squeezy — từng bước (webhook đã tạo trên Lemon)

Repo đã có route **`POST /webhook/lemon-squeezy`** trong `server/src/worker.ts` (cùng Worker với API license). URL bạn gõ trên Lemon phải **trùng worker thật** sau khi deploy, ví dụ:

- Nếu `wrangler.toml` có `name = "bodyexporter-api"` → URL thường là `https://bodyexporter-api.<tài-khoản>.workers.dev/webhook/lemon-squeezy`
- Nếu bạn đổi `name = "bodyexporter"` → `https://bodyexporter.<tài-khoản>.workers.dev/webhook/lemon-squeezy`

### Bước 1 — Đồng bộ URL Lemon với Worker

1. Mở thư mục `server` trên máy, mở `wrangler.toml` xem dòng `name = "..."` là gì.
2. Vào Cloudflare Dashboard → Workers & Pages → chọn đúng worker đó → copy URL `*.workers.dev`.
3. Vào Lemon Squeezy → Settings → Webhooks → sửa webhook: **URL** = `https://<đúng-host-của-worker>/webhook/lemon-squeezy` (HTTPS, không thừa slash).

### Bước 2 — Signing secret (bắt buộc)

1. Trên Lemon, khi tạo webhook bạn đã nhập một chuỗi bí mật (signing secret). Giữ nguyên chuỗi đó.
2. Trên máy dev (trong thư mục `server`):

```powershell
cd server
wrangler secret put LEMON_SQUEEZY_SIGNING_SECRET
```

3. Dán **đúng** chuỗi secret đã nhập trên Lemon (nếu sai 1 ký tự, chữ ký `X-Signature` sẽ không khớp → 401).

### Bước 3 — Chọn sự kiện (events) trên Lemon

Trong webhook, bật tối thiểu:

- **`order_created`** (đơn đã thanh toán xong thường có `status: paid` trong payload), hoặc
- **`order_paid`** nếu Lemon hiển thị trong danh sách (một số gói gửi cả hai).

Worker hiện xử lý **`order_created`** và **`order_paid`** với `data.type === "orders"` và `status` là `paid` hoặc `completed`. Các event khác chỉ trả `{ ok: true, ignored: true }` (vẫn HTTP 200 để Lemon không retry vô hạn).

### Bước 4 — Deploy Worker

```powershell
cd server
wrangler deploy
```

### Bước 5 — Test từ Lemon

1. Lemon → Webhook của bạn → **Send test webhook** (nếu có) hoặc tạo đơn test trong Test mode.
2. Cloudflare → Worker → **Logs** / hoặc chạy `wrangler tail` trong `server` để xem response JSON (`issued: true` và trường `key`).

### Bước 6 — Gửi license tự động (Resend, khuyên dùng)

Worker đã gọi [Resend](https://resend.com) sau khi mint license từ webhook Lemon (nếu bạn cấu hình API key).

1. Tạo tài khoản Resend → **API Keys** → tạo key (bắt đầu bằng `re_`).
2. **Domains** → thêm domain (ví dụ domain bạn dùng với Bravo) → làm đúng bản ghi DNS (SPF/DKIM) mà Resend hiển thị.
3. Trong thư mục `server`:

```powershell
wrangler secret put RESEND_API_KEY
# dán key re_...

wrangler secret put RESEND_FROM
# ví dụ: Body Exporter <orders@ten-mien-cua-ban.com>
```

Nếu không set `RESEND_FROM`, Worker dùng `onboarding@resend.dev` — **chỉ phù hợp test** (Resend thường chỉ cho gửi tới email tài khoản của bạn).

4. `wrangler deploy` lại. Đơn Lemon **paid** → email tới `user_email` trong payload, nội dung có **license key (UUID)**. Response JSON có thêm `resendEmail: { id: "..." }` hoặc `skipped: true` nếu chưa cấu hình Resend.
5. Nếu Resend lỗi, Worker vẫn trả **HTTP 200** (license đã lưu KV) kèm `resendEmail: { ok: false, detail: "..." }` — mở **Lemon → webhook delivery → Response** để đọc lỗi Resend (sai `RESEND_FROM`, domain chưa verify, v.v.). Gửi lại mail thủ công:

```powershell
$api = "https://bodyexporter-api.bodyexporter.workers.dev"
$token = "<ADMIN_TOKEN>"
$body = @{ key = "<license-uuid>" } | ConvertTo-Json
Invoke-RestMethod -Uri "$api/admin/license/send-email" -Method POST `
  -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $body
```

**Lưu ý:** Mở URL webhook bằng trình duyệt (GET) sẽ thấy `{"error":"not_found"}` — bình thường; Lemon chỉ gọi **POST**.

**`RESEND_FROM` đúng:** `Body Exporter <noreply@nesting.click>` (email @ domain đã **Verified** trên Resend). Không dùng `https://nesting.click`. Giai đoạn test có thể dùng `onboarding@resend.dev` nhưng Resend thường **chỉ gửi được tới email tài khoản Resend của bạn**, không phải email khách tùy ý.

**Không dùng Resend:** bỏ secret `RESEND_API_KEY` (hoặc để trống trên Cloudflare). Khi đó chỉ còn gửi tay: xem `wrangler tail`, copy `key`, gửi mail Bravo.

**Lưu ý:** Ô “Apply license” trong app hiện nhận **file JSON đã ký RSA** (LicenseGen). UUID từ server dùng cho **luồng online** (`LicenseKey` + `ApiBaseUrl` trong `settings.json` nếu build của bạn đã nối `LicenseApiClient`). Nếu bạn chỉ dùng file `.lic` offline, sau khi có `key` từ Worker bạn vẫn có thể dùng tool `LicenseGen` để xuất file `.lic` và gửi đính kèm (giống quy trình cũ).

### Bước 7 — `client-config` có link Lemon checkout

Đảm bảo `PUT /admin/client-config` có `paymentIntlLemonsqueezyUrl` trỏ tới link **Buy** của Lemon (variant / checkout). App SolidWorks sẽ hiện nút “Open Lemon Squeezy checkout” trong cửa sổ License (đã code sẵn).

## 8e. SePay (chuyển khoản VN) — QR trong app + webhook tự gửi license

**Trong app:** `paymentVnSepayUrl` dạng `https://qr.sepay.vn/img?bank=ACB&acc=...&amount=990000&des=...` — cửa sổ License hiển thị **ảnh QR + thông tin CK** ngay trong phần “Thanh toán Việt Nam” (không chỉ mở trình duyệt).

**Webhook Worker:** `POST https://<worker-host>/webhook/sepay`

1. Vào [my.sepay.vn](https://my.sepay.vn) → Webhooks → tạo webhook mới.
2. **URL:** `https://bodyexporter-api.bodyexporter.workers.dev/webhook/sepay` (đổi host nếu `wrangler.toml` khác).
3. **Bảo mật:** chọn **một** trong hai (không trộn):
   - **HMAC-SHA256** (SePay gửi `X-SePay-Signature` + `X-SePay-Timestamp`) → Worker cần `SEPAY_WEBHOOK_SECRET`
   - **API Key** (SePay gửi `Authorization: Apikey …`) → Worker cần `SEPAY_WEBHOOK_API_KEY`
   
   Copy secret/key **ngay khi tạo** (SePay chỉ hiện 4 ký tự cuối sau khi lưu).

4. Trên máy dev:

```powershell
cd server
# Nếu webhook SePay = HMAC-SHA256:
wrangler secret put SEPAY_WEBHOOK_SECRET
# Nếu đổi từ API Key sang HMAC, xóa secret cũ để tránh nhầm:
# wrangler secret delete SEPAY_WEBHOOK_API_KEY

wrangler secret put SEPAY_LICENSE_AMOUNT_VND
# nhập cùng số tiền trong paymentVnSepayUrl (vd. 1590000) — fallback nếu URL không có amount=

wrangler secret put SEPAY_LEGACY_AMOUNTS_VND
# tùy chọn: 990000 — vẫn chấp nhận giao dịch cũ sau khi tăng giá

wrangler deploy
```

**Giá hiển thị trong app (QR + dòng “Số tiền”):** lấy từ `paymentVnSepayUrl` trong `PUT /admin/client-config` (vd. `amount=1590000`), **không** tự đổi khi chỉ sửa wrangler secret. Sau khi PUT config, xóa cache app: `%APPDATA%\SolidWorksBodyExporter\client-config-cache.json` hoặc bấm **Check for updates** trong License.

**Webhook tự mint + gửi mail** khi: HMAC đúng, số tiền CK nằm trong danh sách cho phép (amount trên URL config + secret + legacy), memo có email (hoặc dạng `…gmailcom`).

**Kiểm tra secret HMAC khớp SePay (trước khi Phát lại webhook):**

```powershell
node tools/verify-sepay-hmac.mjs "PASTE_SECRET_KEY_FROM_SEPAY"
# phải in match: true với giao dịch mẫu
```

**Lỗi 401 “Xác thực thất bại”:** gần như luôn do `SEPAY_WEBHOOK_SECRET` trên Worker **không trùng** Secret Key trong my.sepay.vn, hoặc chưa `wrangler deploy` sau khi `secret put`. Xem log Worker (Cloudflare dashboard → Workers → Logs) — dòng `Sepay webhook: HMAC mismatch`.

5. **Nội dung chuyển khoản:** khách phải ghi **email** trong memo (vd. `BE 2024hoaphong@gmail.com`). Worker đọc `content` / `description` / `code` từ payload SePay, tìm địa chỉ email, rồi `mintLicense` + gửi mail qua Resend (cùng `RESEND_API_KEY` / `RESEND_FROM` như Lemon).
6. SePay chỉ coi thành công khi response là **HTTP 200** và body đúng `{"success": true}` — route này đã trả đúng format đó.

Nếu sai số tiền hoặc không có email trong memo, Worker vẫn trả `{"success": true}` (để SePay không retry vô hạn) nhưng ghi log KV `sepay-ignored:<id>`. **Phát lại** webhook sau khi deploy bản Worker mới (đã parse `gmailcom` → `@gmail.com`) sẽ xóa `sepay-ignored` và mint + gửi mail — **không cần chuyển khoản mới**.

**Giao dịch cũ đã mint license nhưng chưa nhận mail** (thiếu `RESEND_API_KEY` hoặc replay trước khi cấu Resend):

```powershell
$api = "https://bodyexporter-api.bodyexporter.workers.dev"
$token = "<ADMIN_TOKEN>"
$body = @{ transactionId = 58589721 } | ConvertTo-Json
Invoke-RestMethod -Uri "$api/admin/sepay/resend-email" -Method POST `
  -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $body
```

Hoặc bấm **Phát lại** trên SePay (sau `wrangler deploy`) — Worker gửi lại mail nếu đã có `sepay-tx:58589721` trong KV.

**Bắt buộc cho email tự động:** `wrangler secret put RESEND_API_KEY` và `RESEND_FROM` (domain đã verify trên Resend). Không có Resend thì webhook vẫn 200 + license trong KV nhưng **không gửi mail**.

Cập nhật `paymentVnBody` trong client-config để nhắc khách ghi email trong nội dung CK.

## 9. Revoke license (chargeback hoặc khách đổi máy)

```powershell
$licenseKey = "<key cần revoke>"
Invoke-RestMethod -Uri "$apiUrl/admin/license/$licenseKey" `
    -Method DELETE `
    -Headers @{ Authorization = "Bearer $adminToken" }
```

Client lần refresh JWT tiếp theo sẽ bị 403 → addin tự khoá.

## 10. Monitor traffic

```powershell
wrangler tail
```

Xem logs realtime. Hoặc vào Cloudflare dashboard → Workers → bodyexporter-api → Logs.

---

## Anti-piracy strategy

| Threat | Mitigation |
|---|---|
| Crack DLL bypass license check | Server ký JWT, client validate signature → crack phải sửa cả check signature + ship private key (không có) |
| Share license với máy khác | KV bind 1 license = 1 machineId (trừ plan=floating) |
| Replay JWT cũ | JWT có `exp` 24h, hết hạn phải gọi lại server |
| Spoof server IP | TLS cert → giả mạo bị browser block, client cũng có cert pinning option |
| Reverse engineer business logic | Logic Excel template fill chạy trên server (sẽ implement Phase 2) → client chỉ gửi data, không có source |

## Pricing (Cloudflare free tier)

- Workers: 100k requests/day free, sau đó $5 / 10 triệu requests.
- KV: 100k reads/day, 1k writes/day free, sau đó rẻ.

Tính nhanh: 100 active install × 24h JWT refresh × 4 lần/ngày = 400 requests/day.
Free tier dư sức cho ~250 active install. Lên cỡ 1000+ install thì chuyển sang
plan $5/tháng.
