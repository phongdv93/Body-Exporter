using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorksBodyExporter.AddIn.Services.Security;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>POST anonymous install/usage heartbeat to the marketing site (not Worker).</summary>
    internal static class TelemetryReporter
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromHours(20);

        public static void TrySendConnectPing(SldWorks sw, string installRoot)
        {
            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        SendPing(sw, installRoot, "connect");
                    }
                    catch (WebException wex)
                    {
                        var code = wex.Response is HttpWebResponse hr ? (int)hr.StatusCode : 0;
                        DiagnosticLog.Warn("TelemetryReporter: ping failed HTTP " + code + " - " + wex.Message);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Warn("TelemetryReporter: ping failed - " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("TelemetryReporter: schedule failed - " + ex.Message);
            }
        }

        private static void SendPing(SldWorks sw, string installRoot, string eventName)
        {
            var settings = AppSettings.LoadOrCreate();
            TelemetryConsent.TryImportBundleOrPromote(settings, installRoot);
            settings = AppSettings.LoadOrCreate();

            if (!TelemetryConsent.IsGranted(settings))
            {
                DiagnosticLog.Info("TelemetryReporter: skipped ping (no data-policy consent)");
                return;
            }

            TelemetryConsent.StampConsentInSettings(settings, "ping");
            settings = AppSettings.LoadOrCreate();

            var lastPing = settings.LastTelemetryPingUtc;
            if (lastPing.HasValue && DateTime.UtcNow - lastPing.Value < MinInterval)
            {
                return;
            }

            var cfg = ClientConfigClient.Load(LicenseManager.DefaultApiBaseUrl, forceRefresh: false);
            var site = SiteUrlPolicy.ResolveSiteBaseUrl(cfg?.DownloadPageUrl);
            var url = site + "/api/v1/client/ping";

            var lm = LicenseManager.Current;
            var status = lm.GetStatus();
            var licenseStatus = MapLicenseStatus(status);

            var payload = JsonConvert.SerializeObject(new
            {
                machineId = lm.GetMachineFingerprint(),
                hostname = System.Environment.MachineName ?? "",
                pluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
                swVersion = ReadSwVersion(sw),
                licenseStatus,
                @event = eventName
            });

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.UserAgent = "SolidWorksBodyExporter/" + Assembly.GetExecutingAssembly().GetName().Version;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var bytes = Encoding.UTF8.GetBytes(payload);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using ((HttpWebResponse)request.GetResponse())
            {
                // success — no body required
            }
        }

        private static string MapLicenseStatus(LicenseStatus status)
        {
            if (status == null)
            {
                return "unknown";
            }

            if (!status.IsAllowed)
            {
                if (status.Source == LicenseSource.Expired)
                {
                    return "expired";
                }

                return "none";
            }

            if (status.Source == LicenseSource.Licensed)
            {
                return "licensed";
            }

            if (status.Source == LicenseSource.Trial || status.Source == LicenseSource.FreshTrial)
            {
                return "trial";
            }

            return "unknown";
        }

        private static string ReadSwVersion(SldWorks sw)
        {
            try
            {
                if (sw == null)
                {
                    return "";
                }

                var rev = sw.RevisionNumber();
                return string.IsNullOrWhiteSpace(rev) ? "" : rev.Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
