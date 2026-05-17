using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// User must accept data policy on bodyexporter.com/download before downloading.
    /// Installer ships <c>telemetry-consent.bundle.json</c>; we import it into settings on first run.
    /// </summary>
    internal static class TelemetryConsent
    {
        private const string BundleFileName = "telemetry-consent.bundle.json";
        private const string ConsentFileName = "telemetry-consent.json";

        public static string BundlePathInInstallRoot(string installRoot)
        {
            return string.IsNullOrWhiteSpace(installRoot)
                ? null
                : Path.Combine(installRoot, BundleFileName);
        }

        public static string ConsentPathInAppData()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter",
                ConsentFileName);
        }

        public static bool IsGranted(AppSettings settings)
        {
            if (settings?.TelemetryConsentAcceptedUtc != null)
            {
                return true;
            }

            try
            {
                var path = ConsentPathInAppData();
                if (!File.Exists(path))
                {
                    return false;
                }

                var jo = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                return jo.Value<bool?>("accepted") == true || jo["acceptedUtc"] != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Import bundle from install folder or promote existing consent file into settings.</summary>
        public static void TryImportBundleOrPromote(AppSettings settings, string installRoot)
        {
            if (settings == null || IsGranted(settings))
            {
                return;
            }

            var bundle = BundlePathInInstallRoot(installRoot);
            if (!string.IsNullOrEmpty(bundle) && File.Exists(bundle))
            {
                try
                {
                    var dir = Path.GetDirectoryName(ConsentPathInAppData());
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(bundle, ConsentPathInAppData(), overwrite: true);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Warn("TelemetryConsent: bundle copy failed - " + ex.Message);
                }
            }

            try
            {
                var consentPath = ConsentPathInAppData();
                if (!File.Exists(consentPath))
                {
                    return;
                }

                var jo = JObject.Parse(File.ReadAllText(consentPath, Encoding.UTF8));
                settings.TelemetryConsentAcceptedUtc = DateTime.UtcNow;
                settings.TelemetryConsentVersion = jo.Value<int?>("version") ?? 1;
                settings.Save();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("TelemetryConsent: promote failed - " + ex.Message);
            }
        }
    }
}
