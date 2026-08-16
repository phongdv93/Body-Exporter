using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Persisted user settings used by the API layer. Lives at
    /// <c>%APPDATA%\SolidWorksBodyExporter\settings.json</c>. The file is editable by
    /// hand for power users; the addin also writes to it whenever a fresh license JWT
    /// is fetched so subsequent launches can skip the network call until the cached
    /// token expires.
    /// <para>
    /// IMPORTANT: this file stores a bearer JWT issued by the licensing server. Treat
    /// it as a secret - do not commit it to source control, do not include it in
    /// support bundles. The server expires tokens after <c>TokenLifetimeMinutes</c>
    /// (default 24 h) so a leaked token has limited blast radius.
    /// </para>
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Optional base URL for the licensing/API server (e.g.
        /// <c>https://api.bodyexporter.io</c>). When null/empty the addin runs in
        /// "local-only" mode and uses the offline RSA license file under
        /// <c>%APPDATA%\SolidWorksBodyExporter\license.json</c>.
        /// </summary>
        public string ApiBaseUrl { get; set; }

        /// <summary>
        /// User-typed license key. The first call to the server exchanges this key
        /// for a JWT that's cached in <see cref="CachedToken"/>; subsequent launches
        /// only re-hit the server when the cached JWT is near expiry.
        /// </summary>
        public string LicenseKey { get; set; }

        /// <summary>Current active license UUID on this machine (after renew, older keys are retired).</summary>
        public List<string> AppliedLicenseKeys { get; set; }

        /// <summary>Keys replaced by a renewal stack (support audit only).</summary>
        public List<string> RetiredLicenseKeys { get; set; }

        /// <summary>
        /// Most recently issued JWT from the licensing server. Validated locally on
        /// each launch (signature check against the embedded RSA public key) so an
        /// offline machine can keep working until the token expires.
        /// </summary>
        public string CachedToken { get; set; }

        /// <summary>UTC expiry of <see cref="CachedToken"/>.</summary>
        public DateTime? CachedTokenExpiresUtc { get; set; }

        /// <summary>Subscription/license end date from the server (not the 24h JWT expiry).</summary>
        public DateTime? LicenseExpiresUtc { get; set; }

        /// <summary>Owner email from last successful online validation.</summary>
        public string OnlineOwner { get; set; }

        /// <summary>Plan name from last successful online validation.</summary>
        public string OnlinePlan { get; set; }

        /// <summary>
        /// Hash of the machine fingerprint at the time the token was issued. If the
        /// fingerprint changes (new hardware) we force a re-validation against the
        /// server even if the JWT is still time-valid. Prevents license-file sharing
        /// across machines after the initial activation.
        /// </summary>
        public string TokenBoundMachineHash { get; set; }

        /// <summary>
        /// Last-selected Excel template (.xlsx) for <see cref="Ui.BodyExportWindow"/> "Fill
        /// template" export. When set and the file still exists, the export flow skips the
        /// template picker and only asks for the output save path.
        /// </summary>
        public string ExcelTemplatePath { get; set; }

        /// <summary>Last workbook produced by "Fill Excel template…" (quick reopen in Excel).</summary>
        public string ExcelTemplateLastOutputPath { get; set; }

        /// <summary>When true, Excel/template export re-sorts rows by BOM keyword tiers first.</summary>
        public bool AutoSortBeforeExport { get; set; } = true;

        /// <summary>Email used for VN bank transfer memo (Sepay). Prefilled from license owner when possible.</summary>
        public string PaymentEmail { get; set; }

        /// <summary>Last successful online license validation (UTC). Used for periodic re-check.</summary>
        public DateTime? LastOnlineValidationUtc { get; set; }

        /// <summary>HMAC over sensitive fields; tampering clears online license cache.</summary>
        public string SettingsHmac { get; set; }

        /// <summary>User accepted data policy on bodyexporter.com/download (required for telemetry).</summary>
        public DateTime? TelemetryConsentAcceptedUtc { get; set; }

        /// <summary>Policy version accepted (default 1).</summary>
        public int? TelemetryConsentVersion { get; set; }

        /// <summary>When user dismissed an update offer for this version, do not prompt again.</summary>
        public string DismissedUpdateOfferVersion { get; set; }

        /// <summary>Last UTC time we checked client-config for updates.</summary>
        public DateTime? LastUpdateCheckUtc { get; set; }

        /// <summary>Last successful POST /api/v1/client/ping (UTC).</summary>
        public DateTime? LastTelemetryPingUtc { get; set; }

        /// <summary>
        /// Origin of the customer ERP (e.g. <c>https://erp.example.com</c>). Used for
        /// <c>GET /api/integrations/v1/me</c> and <c>POST /api/integrations/v1/bom/lines</c>.
        /// </summary>
        public string ErpBaseUrl { get; set; }

        /// <summary>
        /// Legacy slot for the ERP API key, kept only so a link made by an older build can be
        /// moved into the sealed per-machine store and cleared from here. Live keys are held by
        /// <c>ErpLinkStore</c>; storing one in this file let the link be copied to another PC.
        /// </summary>
        public string ErpApiKey { get; set; }

        /// <summary>Last product code successfully used when pushing BOM lines.</summary>
        public string ErpLastProductCode { get; set; }

        /// <summary>When true, rows typed as Other are included in Excel export (legacy; prefer bom-types.json).</summary>
        public bool ExportOtherCategoryToExcel { get; set; }

        /// <summary>When true, rows typed as Other are included when pushing to ERP (legacy; prefer bom-types.json).</summary>
        public bool ExportOtherCategoryToErp { get; set; }

        /// <summary>UI language: <c>en</c> (default) or <c>vi</c>.</summary>
        public string UiLanguage { get; set; } = "en";

        /// <summary>Recent Excel export paths (newest first, max 8).</summary>
        [Newtonsoft.Json.JsonProperty("excelExportHistory")]
        public List<ExcelExportHistoryItem> ExcelExportHistory { get; set; } = new List<ExcelExportHistoryItem>();

        public const int MaxExcelExportHistory = 8;

        public static AppSettings LoadOrCreate()
        {
            try
            {
                var path = GetPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    AppSettings parsed;
                    try
                    {
                        var jo = JObject.Parse(
                            json,
                            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore });
                        parsed = jo.ToObject<AppSettings>(JsonSerializer.Create(JsonSettings));
                    }
                    catch
                    {
                        parsed = JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings);
                    }

                    if (parsed != null)
                    {
                        parsed.NormalizeAppliedLicenseKeys();
                        parsed.NormalizeExcelExportHistory();
                        parsed.ApiBaseUrl = Security.ApiUrlPolicy.Normalize(
                            parsed.ApiBaseUrl,
                            Security.ApiUrlPolicy.DefaultApiBaseUrl);
                        if (string.IsNullOrWhiteSpace(parsed.UiLanguage))
                        {
                            parsed.UiLanguage = "en";
                        }

                        return parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("AppSettings: load failed, using defaults - " + ex.Message);
            }
            return new AppSettings();
        }

        /// <summary>Record a saved workbook path (New Excel or Fill template). Newest first, max 8.</summary>
        public static void RememberExcelExport(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                var settings = LoadOrCreate();
                settings.RememberExcelExportPath(filePath.Trim());
                settings.Save();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("AppSettings.RememberExcelExport failed: " + ex.Message);
            }
        }

        public void RememberExcelExportPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            ExcelTemplateLastOutputPath = filePath.Trim();
            if (ExcelExportHistory == null)
            {
                ExcelExportHistory = new List<ExcelExportHistoryItem>();
            }

            ExcelExportHistory.RemoveAll(h =>
                h == null
                || string.IsNullOrWhiteSpace(h.Path)
                || string.Equals(h.Path, filePath, StringComparison.OrdinalIgnoreCase));
            ExcelExportHistory.Insert(0, new ExcelExportHistoryItem
            {
                Path = filePath.Trim(),
                SavedUtc = DateTime.UtcNow
            });
            if (ExcelExportHistory.Count > MaxExcelExportHistory)
            {
                ExcelExportHistory = ExcelExportHistory.Take(MaxExcelExportHistory).ToList();
            }
        }

        public void NormalizeExcelExportHistory()
        {
            if (ExcelExportHistory == null)
            {
                ExcelExportHistory = new List<ExcelExportHistoryItem>();
            }

            ExcelExportHistory = ExcelExportHistory
                .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Path))
                .GroupBy(h => h.Path.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.SavedUtc).First())
                .OrderByDescending(h => h.SavedUtc)
                .Take(MaxExcelExportHistory)
                .ToList();

            // Migrate single last-output path into history if missing.
            if (!string.IsNullOrWhiteSpace(ExcelTemplateLastOutputPath)
                && !ExcelExportHistory.Any(h =>
                    string.Equals(h.Path, ExcelTemplateLastOutputPath, StringComparison.OrdinalIgnoreCase)))
            {
                ExcelExportHistory.Insert(0, new ExcelExportHistoryItem
                {
                    Path = ExcelTemplateLastOutputPath.Trim(),
                    SavedUtc = DateTime.UtcNow
                });
                if (ExcelExportHistory.Count > MaxExcelExportHistory)
                {
                    ExcelExportHistory = ExcelExportHistory.Take(MaxExcelExportHistory).ToList();
                }
            }
        }

        /// <returns>false if the file could not be written.</returns>
        public bool Save()
        {
            try
            {
                var path = GetPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(this, JsonSettings);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("AppSettings: save failed - " + ex.Message);
                return false;
            }
        }

        public static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter",
                "settings.json");
        }

        public void NormalizeAppliedLicenseKeys()
        {
            if (AppliedLicenseKeys == null)
            {
                AppliedLicenseKeys = new List<string>();
            }

            if (!string.IsNullOrWhiteSpace(LicenseKey))
            {
                var k = LicenseKey.Trim();
                if (!AppliedLicenseKeys.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase)))
                {
                    AppliedLicenseKeys.Add(k);
                }
            }
        }

        public bool HasAppliedLicenseKey(string key)
        {
            return IsKnownLicenseKey(key);
        }

        /// <summary>Active or retired — already stacked on this machine; must not add days again.</summary>
        public bool IsKnownLicenseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            NormalizeAppliedLicenseKeys();
            key = key.Trim();
            if (AppliedLicenseKeys != null
                && AppliedLicenseKeys.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (RetiredLicenseKeys != null
                && RetiredLicenseKeys.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        /// <summary>Distinct UUID keys ever applied on this machine (active + retired).</summary>
        public IList<string> GetAllKnownLicenseKeys()
        {
            NormalizeAppliedLicenseKeys();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            void AddKeys(IEnumerable<string> source)
            {
                if (source == null)
                {
                    return;
                }

                foreach (var raw in source)
                {
                    var k = raw?.Trim();
                    if (string.IsNullOrWhiteSpace(k) || !Guid.TryParse(k, out _))
                    {
                        continue;
                    }

                    if (seen.Add(k))
                    {
                        list.Add(k);
                    }
                }
            }

            AddKeys(AppliedLicenseKeys);
            AddKeys(RetiredLicenseKeys);
            return list;
        }

        public void RegisterAppliedLicenseKey(string key)
        {
            SetActiveLicenseKey(key);
        }

        /// <summary>
        /// One active key for validation; previous keys move to <see cref="RetiredLicenseKeys"/>.
        /// Stacked time lives in <see cref="LicenseExpiresUtc"/>.
        /// </summary>
        public void SetActiveLicenseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            NormalizeAppliedLicenseKeys();
            key = key.Trim();

            if (RetiredLicenseKeys == null)
            {
                RetiredLicenseKeys = new List<string>();
            }

            foreach (var existing in AppliedLicenseKeys.ToList())
            {
                if (!string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)
                    && !RetiredLicenseKeys.Any(x => string.Equals(x, existing, StringComparison.OrdinalIgnoreCase)))
                {
                    RetiredLicenseKeys.Add(existing);
                }
            }

            AppliedLicenseKeys = new List<string> { key };
            LicenseKey = key;
        }
    }
}
