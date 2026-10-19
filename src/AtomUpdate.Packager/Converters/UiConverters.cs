using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AtomUpdate.Packager.Converters;

/// <summary>布尔取反（按钮可见性等）。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>布尔 → 字重（当前步骤标题加粗）。</summary>
public sealed class BoolFontConverter : IValueConverter
{
    public static readonly BoolFontConverter Bold = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
