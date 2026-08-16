using System.Reflection;
using System.Windows;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class ErpPushWindow : Window
    {
        public string ProductCode { get; private set; }

        public ErpPushWindow(string initialProductCode, int lineCount, string partLabel)
        {
            InitializeComponent();
            ProductCodeBox.Text = initialProductCode ?? string.Empty;
            var part = string.IsNullOrWhiteSpace(partLabel) ? "current part" : partLabel;
            HintText.Text = "Send " + lineCount + " BOM line(s) from " + part
                            + " to an existing ERP product. Lines replace the CAD section (replaceSection=true).";
            Loaded += (_, __) =>
            {
                ProductCodeBox.Focus();
                ProductCodeBox.SelectAll();
            };
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            var code = (ProductCodeBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show(
                    this,
                    "Product code is required.",
                    "Send BOM to ERP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ProductCode = code;
            DialogResult = true;
            Close();
        }
    }
}
