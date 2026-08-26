using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
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
        ThemeBrush.Named(value switch
        {
            HealthState.Ok => "SystemFillColorSuccessBrush",
            HealthState.Warning => "SystemFillColorCautionBrush",
            HealthState.Critical => "SystemFillColorCriticalBrush",
            _ => "TextFillColorDisabledBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

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

/// <summary>
/// Un contador mayor que cero se muestra. Para insignias tipo «3 en rojo».
///
/// Con «invert» se muestra cuando el contador es cero, que es como se pinta el texto de
/// lista vacía: en WPF eso era un DataTrigger contra Value="0", y aquí no hay triggers.
/// </summary>
public sealed partial class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool any = value is int count && count > 0;

        if (StringToVisibilityConverter.IsInverted(parameter))
        {
            any = !any;
        }

        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Severidad de un evento a pincel, con la misma paleta del sistema que el semáforo.
///
/// Info sale en verde y no en gris a propósito: en el Timeline la mayoría de las líneas son
/// informativas, y en gris se leerían como «no se sabe» en vez de como «esto fue normal».
/// </summary>
public sealed partial class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        ThemeBrush.Named(value switch
        {
            Severity.Critical or Severity.Error => "SystemFillColorCriticalBrush",
            Severity.Warning => "SystemFillColorCautionBrush",
            Severity.Info => "SystemFillColorSuccessBrush",
            _ => "TextFillColorDisabledBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Sector a su etiqueta en castellano, sin duplicar la tabla en el XAML.</summary>
public sealed partial class SectorNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is Sector sector ? SectorNames.Display(sector) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Un valor no nulo se muestra. Para paneles que solo existen tras ejecutar algo.</summary>
public sealed partial class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool present = value is not null;

        if (StringToVisibilityConverter.IsInverted(parameter))
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Los pinceles de tema, por nombre.
///
/// Se buscan en cada conversión y no se guardan en un campo estático: son recursos de tema,
/// y congelar el primero que se pida dejaría los colores del tema claro pintados en el
/// oscuro si Windows cambia de tema con la aplicación abierta.
/// </summary>
internal static class ThemeBrush
{
    public static Brush Named(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? brush) && brush is Brush found
            ? found
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
