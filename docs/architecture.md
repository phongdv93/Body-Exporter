# Architecture Notes

## Add-in

The SolidWorks add-in is responsible for:

- Scanning bodies from the active Part document.
- Reading body sizes from SolidWorks geometry.
- Reading material and color where available.
- Showing the review table.
- Saving user-edited names and dimension mapping back into the `.SLDPRT`.
- Exporting rows to Excel or clipboard.

The add-in should not contain payment secrets, database passwords, Stripe/Paddle secrets, private signing keys, or plan-changing logic.

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
- Last seen timestamp

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
