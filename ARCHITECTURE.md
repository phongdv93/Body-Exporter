# ARCHITECTURE.md

## 2 bản binary, 1 server API

```
┌────────────────────────────────────────────────────────────────────────┐
│  CLIENT MACHINE  (khách hàng)                                          │
│                                                                        │
│   ┌──────────────────┐   load     ┌─────────────────────────────┐      │
│   │   SolidWorks     │ ─────────▶ │ SolidWorksBodyExporter.AddIn │      │
│   │   (SLDWORKS.exe) │            │ .dll  (COM in-process)       │      │
│   └──────────────────┘            └────────────┬─────────────────┘      │
│                                                │ named pipe IPC        │
│   ┌─────────────────────────────────┐ pipe     │                       │
│   │ SolidWorksBodyExporter.Launcher │◀─────────┘                       │
│   │ .exe   (desktop shortcut)       │                                  │
│   └─────────────────────────────────┘                                  │
│                       │                                                │
└───────────────────────┼────────────────────────────────────────────────┘
                        │  HTTPS (RSA-signed JWT)
                        ▼
┌────────────────────────────────────────────────────────────────────────┐
│  CLOUDFLARE WORKER  (server, mày kiểm soát)                            │
│                                                                        │
│   ┌────────────────────┐         ┌─────────────────────────┐           │
│   │ /v1/license/...    │ ───────▶│ KV  LICENSE_DB           │          │
│   │ /admin/license/... │         │ KV  LICENSE_BY_MACHINE   │          │
│   └────────────────────┘         └─────────────────────────┘           │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

## Hai bản binary trên client là gì

### Bản 1: `SolidWorksBodyExporter.AddIn.dll` (COM add-in)

- SolidWorks load vào process của nó. Đây là phần làm việc với COM API: đọc body,
  dimension, material, render preview, save tên bodies.
- COM đăng ký vào registry khi install (xem `tools\Install-Client.ps1`).
- Khi user trong SolidWorks bấm Tools → BodyExporter, addin mở named pipe server,
  chờ launcher kết nối.

### Bản 2: `SolidWorksBodyExporter.Launcher.exe` (standalone)

- WPF desktop app, không phải COM. User bấm icon trên desktop → exe khởi động →
  kết nối qua named pipe vào addin DLL đang chạy bên trong SolidWorks → addin mở
  cửa sổ Body Exporter.
- Bỏ qua hẳn ribbon SolidWorks 2024 (ribbon greying issue).
- Icon đã embed vào exe qua `tools\Build-LauncherIco.ps1`.

**Quan trọng:** chỉ AddIn DLL nói chuyện trực tiếp với SolidWorks. Launcher EXE
không link tới `SolidWorks.Interop.*` — nó chỉ là UI shell mở cửa sổ qua IPC. Việc
này giữ Launcher nhẹ (~70 KB) và không phụ thuộc SolidWorks version.

## Server API làm gì (phase 1 — đã implement)

- **POST `/v1/license/validate`** — client gửi `{key, machineId}`, server kiểm tra
  license trong KV, bind machineId nếu chưa bind, trả về JWT 24h ký RSA-2048.
- **POST `/admin/license/issue`** — mày gọi từ webhook billing để mint license mới
  sau khi khách trả tiền.
- **GET `/admin/license/list`** — list tất cả license + máy nào đang bind.
- **DELETE `/admin/license/:key`** — revoke license (chargeback hoặc khách đổi máy).

## Server API làm gì (phase 2 — tao sẽ làm khi mày confirm)

- **POST `/v1/export/excel`** — client gửi body JSON, server generate .xlsx, trả
  về byte array. Lý do làm trên server: logic format, dimension, naming là "tài
  sản trí tuệ" của mày — chuyển lên server thì kể cả khi DLL bị decompile cũng
  không lấy được business logic.
- **POST `/v1/template/fill`** — client upload template + body JSON, server fill,
  trả về .xlsx. Cùng lý do.

## Bảo vệ IP (anti-piracy)

| Trên client (DLL) — có thể bị decompile | Trên server (Worker) — không decompile được |
|---|---|
| COM interop (đọc body từ SolidWorks) | License validation logic |
| WPF UI rendering | JWT signing key |
| Preview rendering (procedural) | KV database license → machineId |
| HTTP API client | Phase 2: Excel format logic, template parsing |
| JWT signature validation (chỉ public key) | Phase 2: dimension business rules |

Cracker muốn bypass phải:
1. Sửa DLL để skip JWT signature check **VÀ**
2. Tự tạo JWT giả (cần RSA private key — không có).

→ Khả thi nhưng tốn thời gian hơn nhiều so với việc trả phí. Đủ làm nản lòng 95%
casual cracker; phần còn lại không phải target market của mày.

## Settings file

Client lưu config tại `%APPDATA%\SolidWorksBodyExporter\settings.json`:

```json
{
    "ApiBaseUrl": "https://bodyexporter-api.<your-subdomain>.workers.dev",
    "LicenseKey": "1234-abcd-...",
    "CachedToken": "eyJhbGciOi...",
    "CachedTokenExpiresUtc": "2026-05-15T12:00:00Z",
    "TokenBoundMachineHash": "ab12cd34..."
}
```

- `ApiBaseUrl` rỗng → chạy "offline mode" với license file RSA cũ.
- `ApiBaseUrl` có giá trị → addin sẽ hit server để lấy JWT, cache 24h, validate
  signature local mỗi lần khởi động.

## Installer (1-click cho khách)

`tools\Install-Client.ps1`:
1. Copy DLL + EXE vào `%LOCALAPPDATA%\SolidWorksBodyExporter\`.
2. Chạy `regasm.exe` để đăng ký COM.
3. Tạo shortcut desktop trỏ vào Launcher EXE (icon đã embed).
4. Tạo `settings.json` mặc định với `ApiBaseUrl` đã hard-code sẵn URL Worker của mày.

Khách double-click installer → vài giây → mở SolidWorks → addin có sẵn. Lần đầu
khởi động hỏi license key → paste key → gọi server → cache JWT → chạy.
