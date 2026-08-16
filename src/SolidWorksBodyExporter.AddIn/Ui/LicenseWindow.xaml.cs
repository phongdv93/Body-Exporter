using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SolidWorksBodyExporter.AddIn.Services;
using SolidWorksBodyExporter.AddIn.Services.Api;
using SolidWorksBodyExporter.AddIn.Services.Ui;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class LicenseWindow : Window
    {
        private const string DefaultBuyUrl = "https://bodyexporter.com/buy";

        private ClientRemoteConfig _remote = new ClientRemoteConfig();
        private bool _purchaseSectionExpanded;

        public LicenseWindow()
        {
            InitializeComponent();
            Loaded += LicenseWindow_Loaded;
        }

        private void ApplyLocalizedStaticText()
        {
            OpenBuyWebButton.Content = LicenseUiText.OpenBuyOnWeb;
            BuyLicenseButton.Content = LicenseUiText.ActivateLicense;
            ApplyLicenseButton.Content = LicenseUiText.ApplyKeys;
            InstallFromFileButton.Content = LicenseUiText.ChooseLicFile;
            RecalculateStackButton.Content = LicenseUiText.RecalculateDays;
            ActivateSectionHintText.Text = LicenseUiText.ActivateSectionHint;
            LicenseKeysEntry.ToolTip = LicenseUiText.LicenseKeysToolTip;
            RecalculateStackButton.ToolTip = LicenseUiText.RecalculateToolTip;
            LicenseFooterNoteText.Text = LicenseUiText.FooterNote;
        }

        public bool LicenseChanged { get; private set; }

        private void LicenseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LicenseWindow_Loaded;
            try
            {
                ApplyLocalizedStaticText();
                RefreshLicenseKeysText();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("LicenseWindow: deferred init (after layout) failed", ex);
                ShowResult("Could not finish loading: " + ex.Message, isError: true);
            }

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(LoadRemoteConfigSafe));
        }

        private void LoadRemoteConfigSafe()
        {
            try
            {
                var api = AppSettings.LoadOrCreate().ApiBaseUrl;
                if (string.IsNullOrWhiteSpace(api))
                {
                    api = LicenseManager.DefaultApiBaseUrl;
                }

                _remote = ClientConfigClient.Load(api, forceRefresh: true);
                ApplyRemoteConfig(_remote);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("LicenseWindow: remote config - " + ex.Message);
                ApplyRemoteConfig(new ClientRemoteConfig());
            }
        }

        private void ApplyRemoteConfig(ClientRemoteConfig cfg)
        {
            _remote = cfg ?? new ClientRemoteConfig();
            AuthorText.Text = string.IsNullOrWhiteSpace(cfg.AuthorName) ? "Gió" : TextEncodingHelper.NormalizeRemote(cfg.AuthorName);
            SupportEmailText.Text = string.IsNullOrWhiteSpace(cfg.SupportEmail)
                ? LicenseUiText.EmptyField
                : TextEncodingHelper.NormalizeRemote(cfg.SupportEmail);
            SupportUrlText.Text = string.IsNullOrWhiteSpace(cfg.SupportUrl)
                ? LicenseUiText.EmptyField
                : TextEncodingHelper.NormalizeRemote(cfg.SupportUrl);
            var hasUrl = !string.IsNullOrWhiteSpace(cfg.SupportUrl);
            OpenSupportUrlButton.Visibility = hasUrl ? Visibility.Visible : Visibility.Collapsed;

            var buyUrl = ResolveBuyWebUrl(cfg);
            OpenBuyWebButton.ToolTip = buyUrl;
        }

        private static string ResolveBuyWebUrl(ClientRemoteConfig cfg)
        {
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.PaymentWebUrl))
            {
                return cfg.PaymentWebUrl.Trim();
            }

            return DefaultBuyUrl;
        }

        private void OpenSupportUrl_Click(object sender, RoutedEventArgs e)
        {
            TryOpenUrl(_remote?.SupportUrl);
        }

        private void OpenBuyWeb_Click(object sender, RoutedEventArgs e)
        {
            var url = ResolveBuyWebUrl(_remote)?.Trim() ?? DefaultBuyUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = DefaultBuyUrl;
            }

            var email = ResolveLicenseEmail();
            if (!string.IsNullOrWhiteSpace(email) && url.IndexOf('?') < 0)
            {
                url = url + "?email=" + Uri.EscapeDataString(email.Trim());
            }

            TryOpenUrl(url);
        }

        /// <summary>
        /// Email this machine's license is issued to, used to prefill the web checkout so a
        /// renewal lands on the existing key. If nothing is assigned yet (fresh install or a
        /// trial, which has no email), returns empty so the buyer types their own address.
        /// The Sepay-memo <see cref="AppSettings.PaymentEmail"/> is deliberately ignored here:
        /// it can go stale and point at a different person, which would split the renewal.
        /// </summary>
        private static string ResolveLicenseEmail()
        {
            var owner = LicenseManager.Current.GetStatus()?.Owner;
            if (LooksLikeEmail(owner))
            {
                return owner.Trim();
            }

            var settings = AppSettings.LoadOrCreate();
            if (LooksLikeEmail(settings.OnlineOwner))
            {
                return settings.OnlineOwner.Trim();
            }

            return string.Empty;
        }

        private static bool LooksLikeEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var at = trimmed.IndexOf('@');
            return at > 0 && at < trimmed.Length - 1 && trimmed.IndexOf('@', at + 1) < 0;
        }

        private static void TryOpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url.Trim(), UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open link: " + ex.Message, "Body Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var api = AppSettings.LoadOrCreate().ApiBaseUrl;
            if (string.IsNullOrWhiteSpace(api))
            {
                api = LicenseManager.DefaultApiBaseUrl;
            }

            var cfg = ClientConfigClient.Load(api, forceRefresh: true);
            ApplyRemoteConfig(cfg);
            UpdateChecker.CheckForUpdatesInteractive(this, cfg, msg => ShowResult(msg, isError: false));
        }

        private void BuyLicense_Click(object sender, RoutedEventArgs e)
        {
            _purchaseSectionExpanded = !_purchaseSectionExpanded;
            PurchaseSection.Visibility = _purchaseSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyLicense_Click(object sender, RoutedEventArgs e)
        {
            var raw = LicenseKeysEntry.Text ?? string.Empty;
            if (raw.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                if (LicenseManager.Current.TryInstallLicenseContent(raw, out var licError))
                {
                    LicenseChanged = true;
                    ShowResult("License file installed successfully.", isError: false);
                    RefreshLicenseKeysText();
                    RefreshStatus();
                }
                else
                {
                    ShowResult("Could not install license: " + (licError ?? "(unknown error)"), isError: true);
                }

                return;
            }

            var keys = LicenseManager.ParseLicenseKeyLines(raw);
            if (keys.Count == 0)
            {
                ShowResult(LicenseUiText.PasteKeysPrompt, isError: true);
                return;
            }

            if (!LicenseManager.Current.TryActivateNewLicenseKeys(keys, out var error, out var summary))
            {
                ShowResult("Could not install license: " + (error ?? "(unknown error)"), isError: true);
                return;
            }

            LicenseChanged = true;
            RefreshLicenseKeysText();
            RefreshStatus();

            if (summary.NewlyActivated == 0 && summary.SkippedAlreadyApplied > 0)
            {
                if (summary.RecalculatedStack)
                {
                    var recalcStatus = LicenseManager.Current.GetStatus();
                    var recalcDays = recalcStatus.DaysRemaining.HasValue
                        ? recalcStatus.DaysRemaining.Value.ToString(CultureInfo.InvariantCulture)
                        : "?";
                    ShowResult(
                        "All keys already applied. Refreshed from the server using "
                        + summary.KeysInStack + " key(s). " + recalcDays + " day(s) remaining.",
                        isError: false);
                }
                else
                {
                    ShowResult(LicenseUiText.KeysAlreadyApplied, isError: false);
                }

                return;
            }

            var status = LicenseManager.Current.GetStatus();
            var days = status.DaysRemaining.HasValue ? status.DaysRemaining.Value.ToString(CultureInfo.InvariantCulture) : "?";
            if (summary.SkippedAlreadyApplied > 0)
            {
                ShowResult(
                    "Added " + summary.NewlyActivated + " new key(s). "
                    + summary.SkippedAlreadyApplied + " already applied. " + days + " day(s) remaining.",
                    isError: false);
            }
            else
            {
                var msg = "Activated " + summary.NewlyActivated + " new key(s). " + days + " day(s) remaining.";
                if (summary.RetiredPreviousKeys > 0)
                {
                    msg += " (previous keys were merged and archived as Retired).";
                }

                ShowResult(msg, isError: false);
            }
        }

        private void RecalculateStack_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenseManager.Current.TryRecalculateStackedEntitlement(out var error, out var keysStacked))
            {
                ShowResult(error ?? "Could not recalculate.", isError: true);
                return;
            }

            LicenseChanged = true;
            RefreshLicenseKeysText();
            RefreshStatus();
            var status = LicenseManager.Current.GetStatus();
            var days = status.DaysRemaining.HasValue ? status.DaysRemaining.Value.ToString(CultureInfo.InvariantCulture) : "?";
            ShowResult(
                "Refreshed from " + keysStacked + " key(s). " + days + " day(s) remaining (expires: "
                + (status.ExpiresUtc?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?") + ").",
                isError: false);
        }

        private void InstallFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "License file (*.lic)|*.lic|All files (*.*)|*.*",
                Title = "Select license.lic from support",
                InitialDirectory = LicenseManager.Current.LicenseDirectory
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (LicenseManager.Current.TryInstallLicense(dialog.FileName, out var error))
            {
                LicenseChanged = true;
                ShowResult("License installed successfully.", isError: false);
                RefreshLicenseKeysText();
                RefreshStatus();
            }
            else
            {
                ShowResult("Could not install license: " + (error ?? "(unknown error)"), isError: true);
            }
        }

        private void FingerprintBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            TryCopyFingerprint();
        }

        private void TryCopyFingerprint()
        {
            const int attempts = 4;
            Exception last = null;
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    Clipboard.SetText(FingerprintBox.Text ?? string.Empty);
                    ShowResult("Fingerprint copied to clipboard.", isError: false);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(50);
                }
            }

            ShowResult("Copy failed: " + (last?.Message ?? "(unknown)"), isError: true);
        }

        private void RefreshLicenseKeysText()
        {
            var settings = AppSettings.LoadOrCreate();
            var keys = settings.GetAllKnownLicenseKeys();
            LicenseKeysEntry.Text = string.Join(Environment.NewLine, keys);

            if (RecalculateStackButton != null)
            {
                var count = keys.Count();
                RecalculateStackButton.Visibility = count >= 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshStatus()
        {
            var status = LicenseManager.Current.GetStatus();

            FingerprintBox.Text = string.IsNullOrEmpty(status.MachineFingerprint)
                ? LicenseManager.Current.GetMachineFingerprint()
                : status.MachineFingerprint;

            OwnerText.Text = string.IsNullOrEmpty(status.Owner) ? LicenseUiText.EmptyField : status.Owner;
            PlanText.Text = string.IsNullOrEmpty(status.PlanName) ? LicenseUiText.EmptyField : status.PlanName;
            ExpiresText.Text = status.ExpiresUtc.HasValue
                ? status.ExpiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : LicenseUiText.EmptyField;
            DaysText.Text = status.DaysRemaining.HasValue
                ? status.DaysRemaining.Value.ToString(CultureInfo.InvariantCulture)
                : LicenseUiText.EmptyField;

            ApplyBadge(status);
        }

        private void ApplyBadge(LicenseStatus status)
        {
            string title;
            Color background;
            Color foreground;

            switch (status.Source)
            {
                case LicenseSource.Licensed:
                    title = "Licensed";
                    background = (Color)ColorConverter.ConvertFromString("#FFE6F4EA");
                    foreground = (Color)ColorConverter.ConvertFromString("#FF1B5E20");
                    break;
                case LicenseSource.Trial:
                case LicenseSource.FreshTrial:
                    title = "Trial mode";
                    background = (Color)ColorConverter.ConvertFromString("#FFFFF4E5");
                    foreground = (Color)ColorConverter.ConvertFromString("#FFB26A00");
                    break;
                case LicenseSource.TrialExpired:
                case LicenseSource.Expired:
                    title = status.Source == LicenseSource.Expired ? "License expired" : "Trial expired";
                    background = (Color)ColorConverter.ConvertFromString("#FFFDEAEA");
                    foreground = (Color)ColorConverter.ConvertFromString("#FFB71C1C");
                    break;
                case LicenseSource.Tampered:
                    title = "License invalid";
                    background = (Color)ColorConverter.ConvertFromString("#FFFDEAEA");
                    foreground = (Color)ColorConverter.ConvertFromString("#FFB71C1C");
                    break;
                case LicenseSource.WrongMachine:
                    title = "License signed for a different machine";
                    background = (Color)ColorConverter.ConvertFromString("#FFFDEAEA");
                    foreground = (Color)ColorConverter.ConvertFromString("#FFB71C1C");
                    break;
                default:
                    title = "License status";
                    background = (Color)ColorConverter.ConvertFromString("#FFF1F4F9");
                    foreground = (Color)ColorConverter.ConvertFromString("#FF3D5A80");
                    break;
            }

            StatusBadge.Background = new SolidColorBrush(background);
            StatusTitle.Foreground = new SolidColorBrush(foreground);
            StatusDetail.Foreground = new SolidColorBrush(foreground);
            StatusTitle.Text = title;
            StatusDetail.Text = status.Message ?? string.Empty;
        }

        private void ShowResult(string text, bool isError)
        {
            ResultText.Text = text;
            ResultText.Foreground = isError
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB71C1C"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1B6E2A"));
            ResultText.Visibility = Visibility.Visible;
        }
    }
}
