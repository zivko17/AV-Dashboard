using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Console.Controls;

/// <summary>Traduce el semaforo a un pincel. Verde, ámbar, rojo y gris. No hay mas matices.</summary>
public sealed class HealthBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Ok = new(Color.FromRgb(0x3F, 0xBF, 0x6F));
    public static readonly SolidColorBrush Warning = new(Color.FromRgb(0xE0, 0xA8, 0x3C));
    public static readonly SolidColorBrush Critical = new(Color.FromRgb(0xE4, 0x57, 0x4C));
    public static readonly SolidColorBrush Unknown = new(Color.FromRgb(0x5A, 0x64, 0x6E));

    static HealthBrushConverter()
    {
        Ok.Freeze();
        Warning.Freeze();
        Critical.Freeze();
        Unknown.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            HealthState.Ok => Ok,
            HealthState.Warning => Warning,
            HealthState.Critical => Critical,
            _ => Unknown,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Severidad de un evento del Timeline a color, con la misma paleta.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Severity.Critical or Severity.Error => HealthBrushConverter.Critical,
            Severity.Warning => HealthBrushConverter.Warning,
            Severity.Info => HealthBrushConverter.Ok,
            _ => HealthBrushConverter.Unknown,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sector a su etiqueta en castellano, sin duplicar la tabla en el XAML.</summary>
public sealed class SectorNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Sector sector ? SectorNames.Display(sector) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Booleano a visibilidad; con parámetro "invert" al revés.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;

        if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Booleano invertido. Para deshabilitar un botón mientras algo está en curso.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}

/// <summary>Un valor no nulo se muestra. Para paneles que solo existen tras ejecutar algo.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Un contador mayor que cero se muestra. Para insignias tipo «3 alertas».</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Un texto no vacío se muestra; vacío o null se colapsa.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Un color en «#RRGGBB» a pincel.
///
/// El color de carga lo decide Core, que no puede conocer WPF ni WinUI —y que gracias a eso
/// se prueba en el CI de Linux—, asi que viaja como texto y cada interfaz lo convierte. Son
/// estas ocho lineas por interfaz a cambio de que la escala no lineal viva en un solo sitio
/// y este cubierta por pruebas.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || hex.Length != 7)
        {
            return Brushes.Gray;
        }

        // Se cachean porque el panel repinta cada segundo y crear un pincel por celda y por
        // vuelta genera basura para nada: los colores del degradado se repiten.
        if (Cache.TryGetValue(hex, out SolidColorBrush? cached))
        {
            return cached;
        }

        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();

        return Cache[hex] = brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// El semaforo en palabras, para los lectores de pantalla.
///
/// El punto de color de la barra lateral es la unica senal de que una seccion esta en rojo,
/// y el color por si solo no llega: entre el 5 y el 8 % de los hombres no distingue bien el
/// verde del ambar, y quien usa un lector no ve el punto en absoluto. Esto va en
/// AutomationProperties.ItemStatus, que es el sitio para un estado que cambia sin que cambie
/// el nombre del elemento.
/// </summary>
public sealed class HealthNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HealthState health
            ? health switch
            {
                HealthState.Ok => "correcto",
                HealthState.Warning => "conviene revisarlo",
                HealthState.Critical => "afecta a producción",
                _ => "sin datos",
            }
            : "sin datos";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
