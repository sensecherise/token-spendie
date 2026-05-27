using System;
using System.Globalization;
using System.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.ViewModels;

public sealed class RefreshIntervalLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RefreshInterval i ? i.Label() : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.DisplayName() : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeCalmColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Calm) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeWarnColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Warn) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeHotColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Hot) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
