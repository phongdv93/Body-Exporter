using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Services;
using SolidWorksBodyExporter.AddIn.Services.Api;
using SolidWorksBodyExporter.AddIn.Services.Security;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    public enum BomSettingsTab
    {
        ExcelTemplate,
        BomSort,
        BomType,
        Erp
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class BomSettingsWindow : Window
    {
        public ObservableCollection<BomSortTierRow> Tiers { get; } = new ObservableCollection<BomSortTierRow>();

        public ObservableCollection<BomTypeEditRow> Types { get; } = new ObservableCollection<BomTypeEditRow>();

        public BomSettingsWindow(BomSettingsTab initialTab = BomSettingsTab.ExcelTemplate)
        {
            InitializeComponent();
            DataContext = this;
            LoadAll();
            SelectTab(initialTab);
        }

        public static bool Show(Window owner, BomSettingsTab tab = BomSettingsTab.ExcelTemplate)
        {
            var dlg = new BomSettingsWindow(tab) { Owner = owner };
            return dlg.ShowDialog() == true;
        }

        private void SelectTab(BomSettingsTab tab)
        {
            var tag = tab.ToString();
            foreach (TabItem item in SettingsTabs.Items)
            {
                if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    SettingsTabs.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadAll()
        {
            var settings = AppSettings.LoadOrCreate();
            TemplatePathBox.Text = settings.ExcelTemplatePath ?? string.Empty;

            // A link made on another PC cannot be read here, so the fields come up empty and this
            // machine simply looks unlinked.
            var link = ErpLinkStore.Current();
            ErpBaseUrlBox.Text = link?.BaseUrl ?? string.Empty;
            var key = link?.ApiKey ?? string.Empty;
            ErpApiKeyBox.Password = key;
            ErpApiKeyPlainBox.Text = key;

            SelectLanguageCombo(settings.UiLanguage);

            Tiers.Clear();
            foreach (var tier in BodySortRulesService.LoadUserRulesFile().Tiers.OrderBy(t => t.Priority))
            {
                Tiers.Add(BomSortTierRow.FromTier(tier));
            }

            if (Tiers.Count == 0)
            {
                Tiers.Add(new BomSortTierRow { Priority = 10, Label = "Priority group 1" });
            }

            Types.Clear();
            foreach (var type in BomTypesService.Load().Types.OrderBy(t => t.SortOrder))
            {
                Types.Add(BomTypeEditRow.FromDefinition(type));
            }

            ApplyLanguageToChrome(SelectedUiLanguage());
        }

        private void ApplyLanguageToChrome(string language)
        {
            var vi = string.Equals(language, "vi", StringComparison.OrdinalIgnoreCase);
            Title = vi ? "Cài đặt BOM" : "BOM Settings";
            foreach (TabItem item in SettingsTabs.Items)
            {
                var tag = item.Tag as string;
                if (tag == "ExcelTemplate") item.Header = vi ? "Mẫu Excel" : "Excel template";
                else if (tag == "BomSort") item.Header = vi ? "Sắp xếp BOM" : "BOM sort";
                else if (tag == "BomType") item.Header = vi ? "Loại BOM" : "BOM type";
                else if (tag == "Erp") item.Header = vi ? "Kết nối ERP" : "ERP connection";
            }
        }

        private void SelectLanguageCombo(string language)
        {
            var wantVi = string.Equals(language, "vi", StringComparison.OrdinalIgnoreCase);
            foreach (ComboBoxItem item in UiLanguageCombo.Items)
            {
                var tag = item.Tag as string;
                if (wantVi && string.Equals(tag, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    UiLanguageCombo.SelectedItem = item;
                    return;
                }

                if (!wantVi && string.Equals(tag, "en", StringComparison.OrdinalIgnoreCase))
                {
                    UiLanguageCombo.SelectedItem = item;
                    return;
                }
            }
        }

        private string SelectedUiLanguage()
        {
            if (UiLanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return string.Equals(tag, "vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";
            }

            return "en";
        }

        private void UiLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyLanguageToChrome(SelectedUiLanguage());
        }

        private string CurrentErpApiKey()
        {
            return ShowErpKeyCheck.IsChecked == true
                ? (ErpApiKeyPlainBox.Text ?? string.Empty).Trim()
                : (ErpApiKeyBox.Password ?? string.Empty).Trim();
        }

        private void ShowErpKey_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowErpKeyCheck.IsChecked == true)
            {
                ErpApiKeyPlainBox.Text = ErpApiKeyBox.Password;
                ErpApiKeyPlainBox.Visibility = Visibility.Visible;
                ErpApiKeyBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErpApiKeyBox.Password = ErpApiKeyPlainBox.Text ?? string.Empty;
                ErpApiKeyBox.Visibility = Visibility.Visible;
                ErpApiKeyPlainBox.Visibility = Visibility.Collapsed;
            }
        }

        private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel Template (*.xlsx)|*.xlsx",
                Title = "Choose default Excel template",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            TemplatePathBox.Text = dlg.FileName;
        }

        private void ClearTemplate_Click(object sender, RoutedEventArgs e)
        {
            TemplatePathBox.Text = string.Empty;
        }

        private void OpenTemplate_Click(object sender, RoutedEventArgs e)
        {
            TryOpenPath(TemplatePathBox.Text, "template");
        }

        private static void TryOpenPath(string path, string kind)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    "No " + kind + " file is linked yet (or the file was moved).",
                    "BOM Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void AddTier_Click(object sender, RoutedEventArgs e)
        {
            var next = Tiers.Count == 0 ? 10 : Tiers.Max(t => t.Priority) + 10;
            Tiers.Add(new BomSortTierRow
            {
                Priority = next,
                Label = "Priority group " + (Tiers.Count + 1)
            });
        }

        private void RemoveTier_Click(object sender, RoutedEventArgs e)
        {
            if (TiersGrid.SelectedItem is BomSortTierRow row)
            {
                Tiers.Remove(row);
            }
        }

        private void AddType_Click(object sender, RoutedEventArgs e)
        {
            var defs = Types.Select(t => t.ToDefinition()).ToList();
            Types.Add(BomTypeEditRow.FromDefinition(BomTypesService.CreateCustomTemplate(defs)));
        }

        private void RemoveType_Click(object sender, RoutedEventArgs e)
        {
            if (!(TypesGrid.SelectedItem is BomTypeEditRow row))
            {
                return;
            }

            if (row.IsBuiltIn)
            {
                MessageBox.Show(
                    "Built-in types cannot be removed. You can rename them or clear their keywords.",
                    "BOM Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Types.Remove(row);
        }

        private void TestErp_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = (ErpBaseUrlBox.Text ?? string.Empty).Trim();
            var apiKey = CurrentErpApiKey();
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                ErpStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                ErpStatusText.Text = "Enter Base URL and API key first.";
                return;
            }

            try
            {
                ErpStatusText.Foreground = System.Windows.Media.Brushes.DimGray;
                ErpStatusText.Text = "Testing…";
                var me = new ErpBomClient(baseUrl, apiKey).TestConnectionAsync().GetAwaiter().GetResult();
                ErpStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                ErpStatusText.Text = "OK — " + me.DisplayLabel;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("BomSettingsWindow.TestErp failed", ex);
                ErpStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                ErpStatusText.Text = ex.Message;
            }
        }

        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.LoadOrCreate();
                settings.ExcelTemplatePath = string.IsNullOrWhiteSpace(TemplatePathBox.Text)
                    ? null
                    : TemplatePathBox.Text.Trim();
                settings.UiLanguage = SelectedUiLanguage();

                var erpUrl = (ErpBaseUrlBox.Text ?? string.Empty).Trim();
                var erpKey = CurrentErpApiKey();
                if (!string.IsNullOrWhiteSpace(erpUrl) && !string.IsNullOrWhiteSpace(erpKey))
                {
                    // The key goes into the sealed per-machine link, never into settings.json.
                    if (!ErpLinkStore.Save(erpUrl, erpKey))
                    {
                        StatusText.Text = "Could not store the ERP link on this machine.";
                        StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                        return;
                    }

                    settings.ErpBaseUrl = ErpBomClient.NormalizeBaseUrl(erpUrl);
                    settings.ErpApiKey = null;
                }
                else if (string.IsNullOrWhiteSpace(erpUrl) && string.IsNullOrWhiteSpace(erpKey))
                {
                    ErpLinkStore.Clear();
                    settings.ErpBaseUrl = null;
                    settings.ErpApiKey = null;
                }
                else
                {
                    StatusText.Text = "ERP needs both Base URL and API key (or clear both).";
                    StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                    return;
                }

                var file = new BodySortRulesFile
                {
                    Tiers = Tiers
                        .Select(row => row.ToTier())
                        .Where(t => (t.Keywords != null && t.Keywords.Count > 0) || !string.IsNullOrWhiteSpace(t.Label))
                        .OrderBy(t => t.Priority)
                        .ToList()
                };
                BodySortRulesService.SaveUserRulesFile(file);

                var typesFile = new BomTypesFile
                {
                    Types = Types.Select(t => t.ToDefinition()).ToList()
                };
                BomTypesService.Save(typesFile);

                var other = typesFile.Types.FirstOrDefault(t =>
                    string.Equals(t.Id, BomTypeIds.Other, StringComparison.OrdinalIgnoreCase));
                if (other != null)
                {
                    settings.ExportOtherCategoryToExcel = other.IncludeInExcel;
                    settings.ExportOtherCategoryToErp = other.IncludeInErp;
                }

                SettingsIntegrity.Stamp(settings, Services.LicenseManager.Current.GetMachineFingerprint());
                if (!settings.Save())
                {
                    StatusText.Text = "Could not save settings.json.";
                    StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("BomSettingsWindow.SaveAll failed", ex);
                StatusText.Text = "Save failed: " + ex.Message;
                StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class BomTypeEditRow : INotifyPropertyChanged
    {
        private string _id;
        private string _nameEn;
        private string _nameVi;
        private string _erpSection;
        private string _keywordsText;
        private bool _includeInExcel = true;
        private bool _includeInErp = true;
        private bool _isBuiltIn;
        private int _sortOrder;
        private string _backgroundHex;
        private string _foregroundHex;

        public string Id
        {
            get => _id;
            set { if (_id == value) return; _id = value; OnPropertyChanged(); }
        }

        public string NameEn
        {
            get => _nameEn;
            set { if (_nameEn == value) return; _nameEn = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string NameVi
        {
            get => _nameVi;
            set { if (_nameVi == value) return; _nameVi = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string ErpSection
        {
            get => _erpSection;
            set { if (_erpSection == value) return; _erpSection = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string KeywordsText
        {
            get => _keywordsText;
            set { if (_keywordsText == value) return; _keywordsText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public bool IncludeInExcel
        {
            get => _includeInExcel;
            set { if (_includeInExcel == value) return; _includeInExcel = value; OnPropertyChanged(); }
        }

        public bool IncludeInErp
        {
            get => _includeInErp;
            set { if (_includeInErp == value) return; _includeInErp = value; OnPropertyChanged(); }
        }

        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { if (_isBuiltIn == value) return; _isBuiltIn = value; OnPropertyChanged(); }
        }

        public int SortOrder
        {
            get => _sortOrder;
            set { if (_sortOrder == value) return; _sortOrder = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static BomTypeEditRow FromDefinition(BomTypeDefinition def)
        {
            return new BomTypeEditRow
            {
                Id = def?.Id ?? BomTypeIds.Detail,
                NameEn = def?.NameEn ?? string.Empty,
                NameVi = def?.NameVi ?? string.Empty,
                ErpSection = def?.ErpSection ?? string.Empty,
                KeywordsText = def?.KeywordsText ?? string.Empty,
                IncludeInExcel = def?.IncludeInExcel ?? true,
                IncludeInErp = def?.IncludeInErp ?? true,
                IsBuiltIn = def?.IsBuiltIn ?? false,
                SortOrder = def?.SortOrder ?? 100,
                _backgroundHex = def?.BackgroundHex,
                _foregroundHex = def?.ForegroundHex
            };
        }

        public BomTypeDefinition ToDefinition()
        {
            var def = new BomTypeDefinition
            {
                Id = string.IsNullOrWhiteSpace(Id) ? BomTypesService.NewCustomId(Enumerable.Empty<BomTypeDefinition>()) : Id.Trim(),
                NameEn = string.IsNullOrWhiteSpace(NameEn) ? Id : NameEn.Trim(),
                NameVi = string.IsNullOrWhiteSpace(NameVi) ? NameEn : NameVi.Trim(),
                ErpSection = string.IsNullOrWhiteSpace(ErpSection) ? Id : ErpSection.Trim(),
                IncludeInExcel = IncludeInExcel,
                IncludeInErp = IncludeInErp,
                IsBuiltIn = IsBuiltIn,
                SortOrder = SortOrder,
                BackgroundHex = _backgroundHex,
                ForegroundHex = _foregroundHex
            };
            def.KeywordsText = KeywordsText;
            return def;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
