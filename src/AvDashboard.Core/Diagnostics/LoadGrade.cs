namespace AvDashboard.Core.Diagnostics;

/// <summary>
/// Qué se está midiendo. No es decoración: cada magnitud tiene su propia idea de cuándo
/// empieza a doler, y pintarlas todas con la misma escala es lo que hace que un panel deje
/// de mirarse.
/// </summary>
public enum LoadMetric
{
    Cpu,
    Gpu,
    Memory,
    VideoMemory,

    /// <summary>Porcentaje de disco <b>ocupado</b>, no libre.</summary>
    DiskSpace,

    /// <summary>Tiempo de actividad del disco.</summary>
    DiskActivity,

    /// <summary>Ocupación del enlace de red respecto a su velocidad negociada.</summary>
    NetworkLink,
}

/// <summary>
/// Un color sin depender de ningún marco gráfico.
///
/// Core no puede conocer <c>System.Windows.Media.Brush</c> ni <c>Microsoft.UI.Xaml</c>, y
/// además es justo lo que permite que esto se pruebe en el CI de Linux. Cada interfaz tiene
/// un conversor de tres líneas que lo lleva a su propio pincel.
/// </summary>
public readonly record struct LoadColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// El color de un porcentaje de carga, de verde a rojo pasando por ámbar.
///
/// La escala **no es lineal**, y esa es toda la idea. Una CPU al 50 % en un rig de directo
/// va sobrada: pintarla de amarillo porque 50 está a mitad de camino de 100 es contar una
/// alarma que no existe, y un panel que avisa cuando no pasa nada acaba siendo un panel que
/// nadie mira. Cada magnitud declara dónde deja de ir holgada, dónde conviene mirarla y
/// dónde ya es un problema, y entre esos tres puntos el color va progresivo.
///
/// El enlace de red es el caso que mejor lo explica: al 50 % de un gigabit ya conviene
/// mirarlo, porque lo que mata a NDI y a Dante no es saturar el enlace sino acercarse. La
/// misma cifra que en la CPU es tranquilizadora aquí es un aviso.
/// </summary>
public static class LoadGrade
{
    /// <summary>
    /// Los tres puntos de cada magnitud, en porcentaje.
    ///
    /// <c>Holgado</c>: hasta ahí, verde pleno. <c>Codo</c>: ámbar. <c>Limite</c>: rojo
    /// pleno. Entre ellos se interpola, así que el degradado es continuo y no hay saltos.
    /// </summary>
    private static (double Comfortable, double Knee, double Limit) Curve(LoadMetric metric) =>
        metric switch
        {
            // Un rig renderizando va al 40-60 % sin despeinarse. El problema empieza cuando
            // no queda cabecera para el pico de un cambio de escena.
            LoadMetric.Cpu => (60, 82, 95),

            LoadMetric.Gpu => (65, 85, 96),

            // La RAM llena no es un problema hasta que empieza a paginar, y para entonces
            // ya se nota en la imagen.
            LoadMetric.Memory => (65, 85, 95),

            // La VRAM aguanta más cerca del tope antes de doler, pero cuando desborda la
            // caída es en seco.
            LoadMetric.VideoMemory => (70, 88, 97),

            // Quedarse sin disco a mitad de una grabación no tiene arreglo posterior, así
            // que el aviso llega antes de lo que diría el sentido común de un escritorio, y
            // antes que el de la VRAM: de una VRAM justa se sale bajando una resolución, de
            // un disco lleno a las 21:10 no se sale.
            LoadMetric.DiskSpace => (60, 82, 95),

            LoadMetric.DiskActivity => (60, 85, 97),

            // El que más pronto avisa. Lo que tumba NDI y Dante no es saturar el enlace,
            // es acercarse: el jitter se dispara mucho antes del 100 %.
            LoadMetric.NetworkLink => (45, 70, 90),

            _ => (60, 85, 95),
        };

    /// <summary>
    /// Posición en la escala, de 0 (holgado) a 1 (al límite).
    ///
    /// Es la pieza que se prueba: el color sale de aquí, así que comprobando esto se
    /// comprueba el degradado entero sin tener que razonar sobre canales RGB.
    /// </summary>
    public static double Severity(LoadMetric metric, double percent)
    {
        (double comfortable, double knee, double limit) = Curve(metric);

        if (double.IsNaN(percent))
        {
            return 0;
        }

        percent = Math.Clamp(percent, 0, 100);

        if (percent <= comfortable)
        {
            return 0;
        }

        if (percent >= limit)
        {
            return 1;
        }

        // Dos tramos rectos con el codo en 0,5. Una sola recta de 0 a 100 volvería a
        // repartir el color por igual, que es exactamente lo que no se quiere.
        return percent <= knee
            ? 0.5 * (percent - comfortable) / (knee - comfortable)
            : 0.5 + 0.5 * (percent - knee) / (limit - knee);
    }

    /// <summary>
    /// El color de ese porcentaje.
    ///
    /// El degradado se recorre por tono y no interpolando RGB. Mezclando verde y rojo canal
    /// a canal se pasa por un caqui apagado que no se parece a un ámbar de aviso; moviendo
    /// el tono se pasa por el amarillo de verdad, que es lo que el ojo lee como «ojo con
    /// esto». Saturación y luminosidad se quedan fijas para que las siete tarjetas del panel
    /// pesen lo mismo y ninguna llame más la atención por ser más viva.
    /// </summary>
    public static LoadColor ColorFor(LoadMetric metric, double percent)
    {
        double severity = Severity(metric, percent);

        // 142° es un verde que no chilla; 45° el ámbar del codo; 0° el rojo del límite.
        double hue = severity <= 0.5
            ? 142 - (142 - 45) * (severity / 0.5)
            : 45 - 45 * ((severity - 0.5) / 0.5);

        return FromHsl(hue, saturation: 0.62, lightness: 0.50);
    }

    /// <summary>
    /// El mismo juicio en la escala de salud del resto de la aplicación.
    ///
    /// El color es un degradado y no tiene umbrales, pero un indicador de sector sí los
    /// necesita, y tienen que salir de la misma curva: si no, la tarjeta se pinta ámbar
    /// mientras el punto del sector sigue verde y nadie sabe cuál de los dos mirar.
    /// </summary>
    public static Models.HealthState HealthFor(LoadMetric metric, double percent)
    {
        double severity = Severity(metric, percent);

        return severity switch
        {
            >= 0.85 => Models.HealthState.Critical,
            >= 0.5 => Models.HealthState.Warning,
            _ => Models.HealthState.Ok,
        };
    }

    private static LoadColor FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = hue / 60.0;
        double second = chroma * (1 - Math.Abs(sector % 2 - 1));
        double match = lightness - chroma / 2;

        (double r, double g, double b) = sector switch
        {
            < 1 => (chroma, second, 0.0),
            < 2 => (second, chroma, 0.0),
            < 3 => (0.0, chroma, second),
            < 4 => (0.0, second, chroma),
            < 5 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second),
        };

        return new LoadColor(Channel(r + match), Channel(g + match), Channel(b + match));
    }

    private static byte Channel(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
