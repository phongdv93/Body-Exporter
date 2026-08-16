using System.Windows;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    /// <summary>Legacy entry point — opens BOM Settings on the BOM type tab.</summary>
    public partial class BomTypeSettingsWindow : Window
    {
        public BomTypeSettingsWindow()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static bool ShowEditor(Window owner)
        {
            return BomSettingsWindow.Show(owner, BomSettingsTab.BomType);
        }
    }
}
