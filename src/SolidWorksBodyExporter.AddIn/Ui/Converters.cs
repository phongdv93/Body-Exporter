using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    // All converters below are referenced from XAML by literal type name (e.g.
    // <ui:HexToColorConverter x:Key="HexToColorConverter" />), so they must keep their CLR names
    // for the XAML loader to find them. The [Obfuscation] attributes are a safety net for any
    // future obfuscator pass; without them a "rename everything" run would silently break the UI.

    /// <summary>
    /// Parses a "#RRGGBB" hex string into a <see cref="Color"/> so XAML can bind a SolidColorBrush
    /// directly to the appearance hex carried on each row.
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class HexToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && hex.Length == 7 && hex[0] == '#' &&
                byte.TryParse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return Color.FromRgb(r, g, b);
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Formats a millimeter value with up to two trailing decimals while collapsing trailing zeros,
    /// so 80 stays "80 mm", 80.12345 becomes "80.12 mm" and 0 prints as "0 mm".
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class MillimeterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                var rounded = Math.Round(d, 2, MidpointRounding.AwayFromZero);
                return rounded.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class EditingToCursorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Cursors.SizeAll : Cursors.Arrow;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class EditingToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush EditingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0x7B, 0xCE)));
        private static readonly SolidColorBrush IdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? EditingBrush : IdleBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when bound to <c>true</c>, otherwise
    /// <see cref="Visibility.Collapsed"/>. Used to swap a dimension cell between its read-only
    /// label and the editable axis ComboBox based on <c>BodyExportRow.IsEditing</c>.
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Inverse of <see cref="BoolToVisibilityConverter"/>: visible when <c>false</c>, collapsed
    /// when <c>true</c>.
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Shows placeholder text when a bound string is empty (parameter: HideWhenEmpty).</summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var empty = string.IsNullOrWhiteSpace(value as string);
            var hideWhenEmpty = string.Equals(parameter as string, "HideWhenEmpty", StringComparison.Ordinal);
            return hideWhenEmpty
                ? (empty ? Visibility.Visible : Visibility.Collapsed)
                : (empty ? Visibility.Collapsed : Visibility.Visible);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
