using System;
using System.Reflection;
using System.Windows;
using SolidWorksBodyExporter.AddIn.Services;
using SolidWorksBodyExporter.AddIn.Services.Api;
using SolidWorksBodyExporter.AddIn.Services.Security;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class ErpConnectionWindow : Window
    {
        public bool Saved { get; private set; }

        public ErpConnectionWindow()
        {
            InitializeComponent();
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            // Only this machine's own link is shown. On any other PC the boxes stay empty, as if
            // the plugin had just been installed.
            var link = ErpLinkStore.Current();
            BaseUrlBox.Text = link?.BaseUrl ?? string.Empty;
            var key = link?.ApiKey ?? string.Empty;
            ApiKeyBox.Password = key;
            ApiKeyPlainBox.Text = key;
        }

        private string CurrentApiKey()
        {
            return ShowKeyCheck.IsChecked == true
                ? (ApiKeyPlainBox.Text ?? string.Empty).Trim()
                : (ApiKeyBox.Password ?? string.Empty).Trim();
        }

        private void ShowKeyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowKeyCheck.IsChecked == true)
            {
                ApiKeyPlainBox.Text = ApiKeyBox.Password;
                ApiKeyPlainBox.Visibility = Visibility.Visible;
                ApiKeyBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                ApiKeyBox.Password = ApiKeyPlainBox.Text ?? string.Empty;
                ApiKeyBox.Visibility = Visibility.Visible;
                ApiKeyPlainBox.Visibility = Visibility.Collapsed;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = (BaseUrlBox.Text ?? string.Empty).Trim();
            var apiKey = CurrentApiKey();

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusText.Text = "Base URL and API key are required.";
                return;
            }

            try
            {
                if (!ErpLinkStore.Save(baseUrl, apiKey))
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusText.Text = "Could not store the ERP link on this machine.";
                    return;
                }

                var settings = AppSettings.LoadOrCreate();
                settings.ErpBaseUrl = ErpBomClient.NormalizeBaseUrl(baseUrl);
                settings.ErpApiKey = null;
                SettingsIntegrity.Stamp(settings, LicenseManager.Current.GetMachineFingerprint());
                if (!settings.Save())
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusText.Text = "Could not save settings.";
                    return;
                }

                Saved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("ErpConnectionWindow.Save failed", ex);
                StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusText.Text = "Save failed: " + ex.Message;
            }
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = (BaseUrlBox.Text ?? string.Empty).Trim();
            var apiKey = CurrentApiKey();
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusText.Text = "Enter Base URL and API key first.";
                return;
            }

            try
            {
                StatusText.Foreground = System.Windows.Media.Brushes.DimGray;
                StatusText.Text = "Testing…";
                IsEnabled = false;

                var client = new ErpBomClient(baseUrl, apiKey);
                var me = client.TestConnectionAsync().GetAwaiter().GetResult();

                StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                StatusText.Text = "OK — " + me.DisplayLabel;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("ErpConnectionWindow.Test failed", ex);
                StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusText.Text = ex.Message;
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }
}
