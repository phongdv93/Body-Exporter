using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class BomSortRulesWindow : Window
    {
        public ObservableCollection<BomSortTierRow> Tiers { get; } = new ObservableCollection<BomSortTierRow>();

        public BomSortRulesWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadTiers();
        }

        private void LoadTiers()
        {
            Tiers.Clear();
            foreach (var tier in BodySortRulesService.LoadUserRulesFile().Tiers.OrderBy(t => t.Priority))
            {
                Tiers.Add(BomSortTierRow.FromTier(tier));
            }

            if (Tiers.Count == 0)
            {
                Tiers.Add(new BomSortTierRow { Priority = 10, Label = "Priority group 1" });
            }
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = new BodySortRulesFile
                {
                    Tiers = Tiers
                        .Select(row => row.ToTier())
                        .Where(t => t.Keywords != null && t.Keywords.Count > 0 || !string.IsNullOrWhiteSpace(t.Label))
                        .OrderBy(t => t.Priority)
                        .ToList()
                };

                if (file.Tiers.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "Add at least one tier with keywords before saving.",
                        "BOM sort keywords",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                BodySortRulesService.SaveUserRulesFile(file);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Could not save keywords: " + ex.Message,
                    "BOM sort keywords",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static bool ShowEditor(Window owner)
        {
            var dlg = new BomSortRulesWindow { Owner = owner };
            return dlg.ShowDialog() == true;
        }
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class BomSortTierRow : INotifyPropertyChanged
    {
        private int _priority = 10;
        private string _label = string.Empty;
        private string _keywordsText = string.Empty;

        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority == value)
                {
                    return;
                }

                _priority = value;
                OnPropertyChanged();
            }
        }

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value)
                {
                    return;
                }

                _label = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string KeywordsText
        {
            get => _keywordsText;
            set
            {
                if (_keywordsText == value)
                {
                    return;
                }

                _keywordsText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static BomSortTierRow FromTier(BodySortTier tier)
        {
            return new BomSortTierRow
            {
                Priority = tier?.Priority ?? 10,
                Label = tier?.Label ?? string.Empty,
                KeywordsText = tier?.Keywords == null
                    ? string.Empty
                    : string.Join(", ", tier.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)))
            };
        }

        public BodySortTier ToTier()
        {
            return new BodySortTier
            {
                Priority = Priority,
                Label = Label?.Trim(),
                Keywords = ParseKeywords(KeywordsText)
            };
        }

        private static List<string> ParseKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return Regex.Split(text, @"[,;\r\n]+")
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
