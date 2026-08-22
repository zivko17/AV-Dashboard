using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace AvDashboard.Console.Services;

/// <summary>
/// Lo que separa a una ventana de WPF de una aplicación de Windows 11 de verdad, y que no
/// se arregla con estilos: la barra de título, el color de acento y el fondo.
///
/// Son tres cosas y las tres se notan. La barra de título de serie sale en blanco sobre una
/// aplicación oscura —se ve en cuanto abres la ventana—. El acento de serie de WPF no
/// existe, así que una aplicación que no lee el del usuario nunca parece suya. Y el fondo
/// Mica es lo que hace que la ventana se funda con el escritorio en lugar de flotar encima.
///
/// Todo lo de aquí es «si se puede, mejor»: en un Windows 10, o si DWM dice que no, la
/// aplicación se queda exactamente como estaba. Ninguna de estas llamadas es necesaria para
/// que funcione, y una consola de producción no puede dejar de arrancar por un adorno.
/// </summary>
internal static class DesktopChrome
{
    // Atributos de DwmSetWindowAttribute. Los nombres son los de la documentación.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    /// <summary>Mica y el resto del sistema de fondos llegan con Windows 11 22H2.</summary>
    private const int FirstBuildWithMica = 22621;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Se llama desde OnSourceInitialized: antes de eso la ventana no tiene identificador y
    /// las llamadas a DWM no tienen a qué apuntar.
    /// </summary>
    public static void Apply(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            // Barra de título oscura. En un Windows 10 antiguo el atributo era el 19 y el
            // 20 se ignora; no pasa nada, la barra se queda clara y todo lo demás funciona.
            Set(handle, DwmwaUseImmersiveDarkMode, 1);

            // Esquinas redondeadas. Windows 11 ya lo hace solo, pero dejarlo explícito
            // cuesta una línea y sobrevive a que alguien active el modo clásico.
            Set(handle, DwmwaWindowCornerPreference, DwmwcpRound);

            TryMica(window, handle);
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll no está. No es un Windows en el que esto importe.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// Mica, con dos cerrojos. Uno, la versión: pedirle a un Windows 10 un fondo que no
    /// tiene devuelve error, pero es mejor no llegar a preguntar. Y dos, el resultado de la
    /// propia llamada.
    ///
    /// El fondo solo se hace transparente si las dos cosas salen bien. Ese orden importa:
    /// una ventana transparente sin fondo detrás no es traslúcida, es negra, y eso sí sería
    /// romper la aplicación por un adorno.
    /// </summary>
    private static void TryMica(Window window, IntPtr handle)
    {
        if (Environment.OSVersion.Version.Build < FirstBuildWithMica)
        {
            return;
        }

        if (Set(handle, DwmwaSystemBackdropType, DwmsbtMainWindow) != 0)
        {
            return;
        }

        if (HwndSource.FromHwnd(handle) is not HwndSource source || source.CompositionTarget is null)
        {
            return;
        }

        // WPF pinta sobre una superficie opaca por defecto y taparía el fondo del sistema.
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
    }

    private static int Set(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    /// <summary>
    /// Tiñe la aplicación con el acento que el usuario tenga puesto en Personalización.
    ///
    /// La fuente buena es AccentPalette, que es la tabla de ocho tonos que el propio Windows
    /// calcula a partir del color elegido. El que va sobre fondo oscuro es Light2, no el
    /// base: el base está pensado para fondo claro y sobre antracita queda apagado. Es
    /// exactamente el tono que usan los interruptores de Configuración.
    ///
    /// Se modifica el color de los pinceles ya existentes en lugar de sustituirlos en el
    /// diccionario. StaticResource resuelve una sola vez, así que cambiar la entrada no
    /// llegaría a los estilos que ya la han leído; cambiar el color del pincel sí llega a
    /// todos, porque es el mismo objeto.
    /// </summary>
    public static void ApplySystemAccent(ResourceDictionary resources)
    {
        Color accent = ReadAccent() ?? Color.FromRgb(0x60, 0xCD, 0xFF);
        Color background = Color.FromRgb(0x20, 0x20, 0x20);

        Tint(resources, "AccentBrush", accent);
        Tint(resources, "AccentHoverBrush", Blend(accent, background, 0.10));
        Tint(resources, "AccentPressedBrush", Blend(accent, background, 0.22));

        // El texto sobre el acento: negro o blanco, lo que se lea. Con los tonos claros de
        // Windows 11 casi siempre sale negro, pero un acento oscuro forzado a mano existe.
        Tint(resources, "AccentTextBrush", Luminance(accent) > 0.5 ? Colors.Black : Colors.White);
    }

    private static void Tint(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
        }
    }

    private static Color? ReadAccent()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");

            // Ocho tonos de cuatro bytes en orden R, G, B, A, del más claro al más oscuro.
            // Light3, Light2, Light1, base, y luego los tres oscuros.
            if (key?.GetValue("AccentPalette") is byte[] { Length: >= 32 } palette)
            {
                return Color.FromRgb(palette[4], palette[5], palette[6]);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }

        // Sin la paleta queda el color de cristal de DWM, que es el acento sin aclarar.
        // Se aclara a mano para que no se pierda sobre el fondo oscuro.
        try
        {
            Color glass = SystemParameters.WindowGlassColor;

            if (glass.R != 0 || glass.G != 0 || glass.B != 0)
            {
                return Blend(glass, Colors.White, 0.4);
            }
        }
        catch (InvalidOperationException)
        {
        }

        return null;
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    /// <summary>Luminancia percibida, para decidir si encima va texto negro o blanco.</summary>
    private static double Luminance(Color color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
}
