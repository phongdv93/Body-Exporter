# Release security (plugin DLL)

## Không ship bản Debug / Release thường

`.NET` DLL decompile gần như full source (dnSpy, ILSpy). **Khách chỉ nhận** build qua:

```powershell
.\tools\Build-ClientPackage.ps1 -Version "0.8.x" -CreateZip
```

Output: `dist/BodyExporter-v*-client/` — DLL lấy từ `bin\Release\net48\obfuscated\` (Obfuscar).

Release build **mặc định bật Obfuscate** (`SolidWorksBodyExporter.AddIn.csproj`). Tắt tạm: `-p:Obfuscate=false`.

## Obfuscar làm gì

- Đổi tên **private / internal** (LicenseManager, JWT, API client, export logic…)
- Giữ **public** COM + XAML + model binding (SolidWorks / WPF bắt buộc)
- **Không** string encryption toàn assembly (WPF BAML hay vỡ)

DLL obfuscated khó đọc hơn nhiều; **không** chống được reverse engineer có kinh nghiệm — server vẫn là nguồn sự thật.

## License online (bản mới)

| Trước | Sau |
|-------|-----|
| Re-check ~7 ngày | **Mỗi lần** `GetStatus` khi có mạng (`OnlineRecheckDays = 0`) |
| Offline grace 7 ngày | **1 ngày** sau lần validate OK cuối |
| Chỉ check khi mở cửa sổ | **ConnectToSW** (mỗi session SW) gọi `EnsureStartupOnlineValidation()` |

Key bị revoke trên Worker → lần mở SolidWorks tiếp theo (có internet) fail nhanh hơn.

## API URL trong DLL

Default API host không còn literal plaintext — decode qua `EmbeddedEndpoints` (XOR). Obfuscar rename thêm class/method.

## Việc làm sau (ưu tiên thấp hơn)

- Logic export “core” trên Worker (chỉ plugin không clone được)
- ConfuserEx control-flow (test kỹ COM/WPF trước khi bật)
- Code signing installer

## Kiểm tra nhanh sau build

```powershell
# Tên private method không còn lộ trong DLL ship
Select-String -Path "src\SolidWorksBodyExporter.AddIn\bin\Release\net48\obfuscated\*.dll" -Pattern "ValidateAsync|GetMachineFingerprint" -Encoding byte
# (hoặc mở obfuscated DLL bằng ILSpy — tên method private phải là a/b/c…)
```
