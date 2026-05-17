using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;
using Newtonsoft.Json;

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
        /// <summary>
        /// Fetches the manifest, shows a message when the app is already up to date (or when
        /// the manifest URL is missing), and prompts when a newer version exists.
        /// </summary>
        public static void CheckForUpdatesInteractive(Window owner, string manifestUrl, Action<string> onStatusMessage)
        {
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
                              ? "Open the download page?"
                              : manifest.ReleaseNotes);

                if (MessageBox.Show(owner, msg, "Body Exporter - Update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = manifest.DownloadUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UpdateChecker: " + ex.Message);
                MessageBox.Show(owner, "Could not check for updates: " + ex.Message, "Body Exporter",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static void CheckAndPrompt(Window owner, string manifestUrl)
        {
            CheckForUpdatesInteractive(owner, manifestUrl, null);
        }

        /// <summary>
        /// One-shot check using manifest URL and/or <paramref name="latestVersion"/> from client-config.
        /// Returns true if a newer version exists.
        /// </summary>
        public static bool TryFindNewerVersion(
            string manifestUrl,
            string latestVersion,
            out Version remoteVersion,
            out string downloadUrl,
            out string releaseNotes)
        {
            remoteVersion = null;
            downloadUrl = null;
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
                        downloadUrl = manifest.DownloadUrl;
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
            if (config == null)
            {
                return;
            }

            var manifestUrl = config.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl)
                && !string.IsNullOrWhiteSpace(config.LatestVersion)
                && string.IsNullOrWhiteSpace(config.DownloadPageUrl))
            {
                return;
            }

            if (!TryFindNewerVersion(
                    manifestUrl,
                    config.LatestVersion,
                    out var remote,
                    out var downloadUrl,
                    out var notes))
            {
                return;
            }

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            var msg = "Ban moi: " + remote + " (dang dung " + current + ")." + Environment.NewLine + Environment.NewLine
                      + (string.IsNullOrWhiteSpace(notes) ? "Mo trang tai ve ban moi?" : notes);

            if (MessageBox.Show(owner, msg, "Body Exporter - Cap nhat", MessageBoxButton.YesNo, MessageBoxImage.Information)
                != MessageBoxResult.Yes)
            {
                return;
            }

            var link = !string.IsNullOrWhiteSpace(downloadUrl)
                ? downloadUrl
                : config.DownloadPageUrl;
            if (!string.IsNullOrWhiteSpace(link))
            {
                Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
            }
        }

        private static string HttpGet(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 10000;
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(UpdateChecker).Assembly.GetName().Version;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
