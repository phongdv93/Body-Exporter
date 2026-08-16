using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Lightweight update probe: fetches a JSON manifest URL (from remote config), compares
    /// assembly version, prompts user to open the download page. Silent auto-install without
    /// a code-signed installer is intentionally NOT implemented — Windows SmartScreen would
    /// block arbitrary EXE downloads from the internet; document that in SETUP-SERVER.md.
    /// </summary>
    public static class UpdateChecker
    {
        public const string DefaultDownloadPageUrl = "https://bodyexporter.com/download";

        /// <summary>
        /// Fetches the manifest, shows a message when the app is already up to date (or when
        /// the manifest URL is missing), and prompts when a newer version exists.
        /// </summary>
        public static void CheckForUpdatesInteractive(
            Window owner,
            ClientRemoteConfig config,
            Action<string> onStatusMessage)
        {
            if (!IsRegisteredMachine())
            {
                onStatusMessage?.Invoke(
                    "Máy này chưa được đăng ký. Kích hoạt license hoặc bản dùng thử trước khi kiểm tra cập nhật.");
                return;
            }

            var manifestUrl = config?.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                onStatusMessage?.Invoke("No update manifest URL is configured on the server. Cannot check for updates.");
                return;
            }

            try
            {
                var json = HttpGet(manifestUrl);
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                {
                    onStatusMessage?.Invoke("Update manifest did not contain a version.");
                    return;
                }

                var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                if (!Version.TryParse(manifest.Version.Trim(), out var remote))
                {
                    onStatusMessage?.Invoke("Update manifest has an invalid version string.");
                    return;
                }

                if (remote <= current)
                {
                    onStatusMessage?.Invoke("You are running the latest version.");
                    return;
                }

                var msg = "A newer version is available: " + manifest.Version + " (you have " + current + ")." +
                          Environment.NewLine + Environment.NewLine +
                          (string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                              ? "Open the download page on bodyexporter.com?"
                              : manifest.ReleaseNotes);

                if (MessageBox.Show(owner, msg, "Body Exporter - Update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                OpenDownloadPage(config);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UpdateChecker: " + ex.Message);
                MessageBox.Show(owner, "Could not check for updates: " + ex.Message, "Body Exporter",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static void CheckAndPrompt(Window owner, ClientRemoteConfig config)
        {
            CheckForUpdatesInteractive(owner, config, null);
        }

        public static string ResolveDownloadPageUrl(ClientRemoteConfig config)
        {
            var url = config?.DownloadPageUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url.Trim();
            }

            return DefaultDownloadPageUrl;
        }

        public static void OpenDownloadPage(ClientRemoteConfig config)
        {
            var link = ResolveDownloadPageUrl(config);
            Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
        }

        /// <summary>
        /// One-shot check using manifest URL and/or <paramref name="latestVersion"/> from client-config.
        /// Returns true if a newer version exists.
        /// </summary>
        public static bool TryFindNewerVersion(
            string manifestUrl,
            string latestVersion,
            out Version remoteVersion,
            out string releaseNotes)
        {
            remoteVersion = null;
            releaseNotes = null;

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            if (!string.IsNullOrWhiteSpace(manifestUrl))
            {
                try
                {
                    var json = HttpGet(manifestUrl);
                    var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                    if (manifest != null
                        && !string.IsNullOrWhiteSpace(manifest.Version)
                        && Version.TryParse(manifest.Version.Trim(), out var fromManifest)
                        && fromManifest > current)
                    {
                        remoteVersion = fromManifest;
                        releaseNotes = manifest.ReleaseNotes;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Warn("UpdateChecker manifest: " + ex.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(latestVersion)
                && Version.TryParse(latestVersion.Trim(), out var fromConfig)
                && fromConfig > current)
            {
                remoteVersion = fromConfig;
                return true;
            }

            return false;
        }

        /// <summary>Prompt once per session when a newer build is published on the server.</summary>
        public static void PromptIfNewerAvailable(Window owner, ClientRemoteConfig config)
        {
            if (config == null || !IsRegisteredMachine())
            {
                return;
            }

            if (!TryFindNewerVersion(
                    config.UpdateManifestUrl,
                    config.LatestVersion,
                    out var remote,
                    out var notes))
            {
                return;
            }

            var settings = AppSettings.LoadOrCreate();
            var remoteLabel = remote.ToString();
            if (!string.IsNullOrWhiteSpace(settings.DismissedUpdateOfferVersion)
                && string.Equals(settings.DismissedUpdateOfferVersion.Trim(), remoteLabel, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            var msg = "Phiên bản mới: " + remote + " (bạn đang dùng " + current + ")." + Environment.NewLine + Environment.NewLine
                      + (string.IsNullOrWhiteSpace(notes)
                          ? "Mở trang tải bodyexporter.com/download?"
                          : notes);

            var result = MessageBox.Show(
                owner,
                msg,
                "Body Exporter — Cập nhật",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Cancel)
            {
                settings.DismissedUpdateOfferVersion = remoteLabel;
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                settings.Save();
                return;
            }

            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.Save();

            if (result == MessageBoxResult.Yes)
            {
                OpenDownloadPage(config);
            }
        }

        /// <summary>Background-friendly check (ConnectToSW / window open). Uses cached config unless stale.</summary>
        public static void CheckForUpdatesIfStale(Window owner, bool forceRefresh = false)
        {
            try
            {
                var settings = AppSettings.LoadOrCreate();
                if (!forceRefresh
                    && settings.LastUpdateCheckUtc.HasValue
                    && (DateTime.UtcNow - settings.LastUpdateCheckUtc.Value).TotalHours < 6)
                {
                    return;
                }

                var cfg = ClientConfigClient.Load(LicenseManager.DefaultApiBaseUrl, forceRefresh: forceRefresh);
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                settings.Save();
                PromptIfNewerAvailable(owner, cfg);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UpdateChecker.CheckForUpdatesIfStale: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether this machine is one the server already knows — it holds a license bound to this
        /// fingerprint, or it is inside its trial. An unknown machine does not probe for updates
        /// at all: the server would refuse it, and asking would only leak that it exists.
        /// </summary>
        private static bool IsRegisteredMachine()
        {
            try
            {
                return LicenseManager.Current.GetStatus()?.IsAllowed == true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UpdateChecker: could not read licence status: " + ex.Message);
                return false;
            }
        }

        private static string HttpGet(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 10000;
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(UpdateChecker).Assembly.GetName().Version;
            ApplyMachineIdentity(request);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Identifies the machine asking for the manifest, so the server can serve only the ones
        /// it has on record.
        /// </summary>
        private static void ApplyMachineIdentity(HttpWebRequest request)
        {
            try
            {
                var fingerprint = LicenseManager.Current.GetMachineFingerprint();
                if (!string.IsNullOrWhiteSpace(fingerprint))
                {
                    request.Headers["X-Machine-Id"] = fingerprint;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UpdateChecker: no machine id for the update request: " + ex.Message);
            }
        }
    }
}
