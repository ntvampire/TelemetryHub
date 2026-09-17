using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KsitalTelemetryHub.UI.WinUI.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOk = value is bool b && b;
        return new SolidColorBrush(isOk ? Color.FromArgb(255, 46, 204, 113) : Color.FromArgb(255, 231, 76, 60));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
