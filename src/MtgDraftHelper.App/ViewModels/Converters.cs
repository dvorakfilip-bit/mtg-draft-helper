using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MtgDraftHelper.App.ViewModels;

public static class Converters
{
    /// <summary>Podíl 0–1 na výšku sloupce grafu (30 px).</summary>
    public static readonly IValueConverter FractionToHeight30 = new FractionToHeight(30);

    public static readonly IValueConverter InverseBoolToVisibility = new Lambda(v => v is true ? Visibility.Collapsed : Visibility.Visible);

    public static readonly IValueConverter CountToVisibility = new Lambda(v => v is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed);

    private sealed class Lambda(Func<object, object> convert) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => convert(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class FractionToHeight(double max) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? Math.Clamp(d, 0, 1) * max : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
