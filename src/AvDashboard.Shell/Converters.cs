using AvDashboard.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AvDashboard.Shell;

/// <summary>
/// El semáforo, con los pinceles del sistema y no con cuatro colores elegidos a mano.
///
/// SystemFillColorSuccess, Caution y Critical son recursos de tema de WinUI: cambian solos
/// entre claro y oscuro y son los mismos que usa Windows para decir lo mismo. La versión de
/// WPF llevaba cuatro <c>SolidColorBrush</c> congelados que había que revisar a mano cada
/// vez que se tocaba el fondo.
/// </summary>
public sealed partial class HealthBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Resource(value switch
        {
            HealthState.Ok => "SystemFillColorSuccessBrush",
            HealthState.Warning => "SystemFillColorCautionBrush",
            HealthState.Critical => "SystemFillColorCriticalBrush",
            _ => "TextFillColorDisabledBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? brush) && brush is Brush found
            ? found
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}

/// <summary>Un texto vacío se colapsa. Con el parámetro «invert», al revés.</summary>
public sealed partial class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasText = !string.IsNullOrWhiteSpace(value as string);

        if (IsInverted(parameter))
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    internal static bool IsInverted(object parameter) =>
        parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Un texto no vacío a true. Para InfoBar.IsOpen, que es booleano y no visibilidad: en WPF
/// esto se resolvía con el mismo conversor porque todo era Visibility.
/// </summary>
public sealed partial class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasText = !string.IsNullOrWhiteSpace(value as string);
        return StringToVisibilityConverter.IsInverted(parameter) ? !hasText : hasText;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Booleano a visibilidad; con «invert», al revés.</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;

        if (StringToVisibilityConverter.IsInverted(parameter))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Booleano invertido. Para deshabilitar un control mientras algo está en curso.</summary>
public sealed partial class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not bool flag || !flag;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not bool flag || !flag;
}

/// <summary>Un contador mayor que cero se muestra. Para insignias tipo «3 en rojo».</summary>
public sealed partial class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
