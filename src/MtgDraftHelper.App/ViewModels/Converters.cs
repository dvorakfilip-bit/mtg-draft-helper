using System.Globalization;
using System.Windows.Data;

namespace MtgDraftHelper.App.ViewModels;

public static class Converters
{
    /// <summary>Podíl 0–1 na výšku sloupce grafu (30 px).</summary>
    public static readonly IValueConverter FractionToHeight30 = new FractionToHeight(30);

    private sealed class FractionToHeight(double max) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? Math.Clamp(d, 0, 1) * max : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
