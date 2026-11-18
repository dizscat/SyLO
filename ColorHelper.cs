using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SyLO;

public static class ColorHelper
{
    public static Color Parse(string? s)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(s) && ColorConverter.ConvertFromString(s) is Color c) return c;
        }
        catch { }
        return Colors.White;
    }

    public static SolidColorBrush Brush(string? s, double opacity = 1)
    {
        var b = new SolidColorBrush(Parse(s)) { Opacity = opacity };
        b.Freeze();
        return b;
    }
}

public sealed class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ColorHelper.Brush(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
