# Architecture Notes

## Add-in

The SolidWorks add-in is responsible for:

- Scanning bodies from the active Part document.
- Reading body sizes from SolidWorks geometry.
- Reading material and color where available.
- Showing the review table.
- Saving user-edited names and dimension mapping back into the `.SLDPRT`.
- Exporting rows to Excel or clipboard.
- Pushing BOM lines to a customer ERP (`POST /api/integrations/v1/bom/lines`) using a CAD API key.

The add-in should not contain payment secrets, database passwords, Stripe/Paddle secrets, private signing keys, or plan-changing logic.

## ERP BOM push

Configure under **Export ▾ → ERP connection…**:

| Setting | Source |
|---------|--------|
| Base URL | ERP site origin (e.g. `https://erp.example.com`) |
| API key | ERP → BOM setup → CAD connection (`hp_live_…`) |

Flow:

1. `GET {base}/api/integrations/v1/me` — connection test (`X-API-Key`)
2. User enters existing `productCode`
3. `POST {base}/api/integrations/v1/bom/lines` with:
   - `replaceSection: true`, `source: "body-exporter"`
   - `productLengthMm` / `productWidthMm` / `productHeightMm` (overall part box, L≥W≥H mm)
   - lines: `partCode`, `partName`, `section`, `material`, `qty`, L/W/T, `remark`

**Row Type → ERP `section`:** Chi tiết → `wood` | Vật tư → `hardware` | Bao bì → `packaging`  
Material goes in field `material` (not `section`). `remark` is appearance + part file name.

**Excel:** new workbook splits sheets by Type (Chi tiết always; Vật tư / Bao bì when present). Templates use `{{Type}}`, `{{ProductLength}}`, `{{ProductWidth}}`, `{{ProductHeight}}`, `{{ProductSize}}`.

Credentials live in `%APPDATA%\SolidWorksBodyExporter\settings.json` (`erpBaseUrl`, `erpApiKey`); the API key is included in the DPAPI settings seal.

## Body Metadata Persistence

The current MVP stores JSON in the Part custom property:

`SBE_BodyExportMetadata`

This keeps user-controlled data inside the SolidWorks file:

- Plugin body ID
- Last known SolidWorks body name
- User display name
- Length/Width/Thickness mapping
- Last known X/Y/Z size
- Material and color snapshot
- BOM category (Chi tiết / Vật tư / Bao bì / Khác)
- Last seen timestamp

**Type tags:** Chi tiết (green), Vật tư (orange), Bao bì (purple), Khác (gray). Click column header **Type ▾** after multi-select to apply in bulk. **Khác** is omitted from Excel/ERP unless enabled under Export → BOM type settings.

Future improvement: attach metadata with SolidWorks Attribute API directly to each body or owning feature, then keep the custom property as an index and migration backup.

## License Server

Do not use Google Sheets as the real license server for paid software. Google Sheets has weak access control for this use case, unreliable request limits, no proper signed license flow, and cannot safely protect business logic.

Recommended production backend:

- `POST /license/activate`
- `POST /license/check`
- `POST /license/deactivate`
- `POST /billing/webhook`
- `GET /account/plan`

The server owns:

- Trial start and end dates
- Active subscriptions
- Payment webhook processing
- Machine activation count
- License revocation
- Token signing keys
- Anti-abuse checks

The add-in owns:

- Machine fingerprint collection
- Login/license key entry UI
- Calling the license API
- Caching signed license tokens
- Enforcing online check and offline grace period

## Where Important Code Should Live

Important code should be split like this:

- SolidWorks geometry logic: inside the add-in, because it must call SolidWorks API locally.
- License decision logic: on the server, because client code can be inspected or patched.
- Payment and plan upgrade logic: on the server, triggered by payment provider webhooks.
- Private keys and secrets: server only, never in the add-in.
- Obfuscation and code signing: release pipeline.

The add-in can be obfuscated, but never depend on obfuscation as the only protection.
