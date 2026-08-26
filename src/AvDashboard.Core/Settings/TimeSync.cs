using System.Globalization;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Settings;

/// <summary>Cómo de vieja es la última sincronía correcta del reloj.</summary>
public enum TimeSyncFreshness
{
    /// <summary>No se ha podido leer. No es lo mismo que no haber sincronizado nunca.</summary>
    Unknown,

    /// <summary>Nunca ha sincronizado. El reloj lleva desde la instalación yendo solo.</summary>
    Never,

    /// <summary>Dentro de la ventana. El reloj está disciplinado.</summary>
    Fresh,

    /// <summary>Hace demasiado. El servicio dice que sincroniza, pero no lo está haciendo.</summary>
    Stale,
}

/// <summary>Un servidor de la lista de <c>NtpServer</c>, con sus banderas.</summary>
public sealed record NtpPeer(string Host, uint Flags)
{
    /// <summary>0x01 — pregunta cada <c>SpecialPollInterval</c> y no según el algoritmo.</summary>
    public bool SpecialInterval => (Flags & 0x01) != 0;

    /// <summary>0x02 — solo se usa si no hay nadie mejor.</summary>
    public bool FallbackOnly => (Flags & 0x02) != 0;

    /// <summary>0x04 — modo simétrico activo. En un rig no se usa.</summary>
    public bool SymmetricActive => (Flags & 0x04) != 0;

    /// <summary>0x08 — modo cliente, que es el normal.</summary>
    public bool Client => (Flags & 0x08) != 0;
}

/// <summary>
/// La sincronía de hora vista desde un rig.
///
/// La pregunta útil no es «¿está configurado el servicio?» sino **«¿cuándo puso alguien el
/// reloj en hora por última vez?»**. Windows deja el servicio en manual y lo arranca con un
/// desencadenador y una tarea semanal, y ese arreglo falla en silencio: si el rig está en
/// una VLAN sin salida, el servidor NTP no contesta, la tarea se da por hecha y nadie se
/// entera. Lo único que delata eso es la fecha de la última sincronía buena, que Windows
/// guarda en <c>Config\LastKnownGoodTime</c> aunque el servicio esté parado.
///
/// Que la hora esté mal se paga en tres sitios distintos: los registros de varios nodos
/// dejan de poder cruzarse cuando algo falla, los certificados de cualquier cosa que hable
/// TLS empiezan a dar errores raros, y todo lo que compara marcas de tiempo entre máquinas
/// —la conversación entre la producción de Watchout y sus display servers, por ejemplo—
/// pierde la referencia común. Nada de eso es la sincronía de reproducción, que va por PTP
/// y por su cuenta; es el suelo sobre el que se apoya lo demás.
/// </summary>
public static class TimeSync
{
    /// <summary>
    /// A partir de aquí la sincronía es vieja.
    ///
    /// Una semana es lo que tarda en dispararse la tarea programada de Windows: si ha pasado
    /// más, no es que el reloj derive un poco, es que el mecanismo entero no está corriendo.
    /// La deriva en sí no es el problema —un oscilador decente se va unos segundos al mes—
    /// sino lo que significa que nadie la haya corregido.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    /// <summary>Valor canónico. Es contra esto que compara el catálogo.</summary>
    public static string Canonical(TimeSyncFreshness freshness) => freshness switch
    {
        TimeSyncFreshness.Never => "nunca",
        TimeSyncFreshness.Fresh => "reciente",
        TimeSyncFreshness.Stale => "antigua",
        _ => "desconocida",
    };

    /// <summary>El único estado que se acepta en un rig.</summary>
    public const string Target = "reciente";

    /// <summary>El arranque que se quiere para W32Time.</summary>
    public const string TargetStartup = "automatico";

    /// <summary>El camino de vuelta: dejárselo otra vez al desencadenador de Windows.</summary>
    public const string ManualStartup = "manual";

    /// <summary>
    /// El modo de arranque de un servicio como valor canónico.
    ///
    /// Vive aquí y no en un sitio general porque solo hay una fila de servicio en el
    /// catálogo. El día que haya más, se muda; adelantarse sería inventar una capa para un
    /// único usuario.
    /// </summary>
    public static string? StartupCanonical(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed => TargetStartup,
        ServiceStartMode.Manual => ManualStartup,
        ServiceStartMode.Disabled => "desactivado",
        ServiceStartMode.Boot or ServiceStartMode.System => "sistema",
        _ => null,
    };

    /// <summary>
    /// La fecha guardada como FILETIME, o null si no hay ninguna.
    ///
    /// Cero significa «nunca», y es un valor legítimo que hay que distinguir de «no se deja
    /// leer». Fuera de rango también devuelve null en vez de lanzar: el valor lo escribe
    /// Windows, pero lo puede haber tocado cualquiera con un editor de registro.
    /// </summary>
    public static DateTimeOffset? FromFileTime(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cómo de vieja es.
    ///
    /// Una fecha en el futuro cuenta como reciente y no como error: pasa cuando el reloj se
    /// adelanta y luego se corrige, y montar un caso aparte para eso sería ruido.
    /// </summary>
    public static TimeSyncFreshness Rate(DateTimeOffset? lastGood, DateTimeOffset now)
    {
        if (lastGood is null)
        {
            return TimeSyncFreshness.Never;
        }

        return now - lastGood.Value <= StaleAfter
            ? TimeSyncFreshness.Fresh
            : TimeSyncFreshness.Stale;
    }

    /// <summary>
    /// Cuánto hace, en palabras.
    ///
    /// El número importa más que la fecha: «hace 82 días» se entiende de un vistazo y «5 de
    /// junio» obliga a restar mentalmente delante del cliente.
    /// </summary>
    public static string DescribeAge(DateTimeOffset? lastGood, DateTimeOffset now)
    {
        if (lastGood is null)
        {
            return "Nunca ha sincronizado";
        }

        TimeSpan age = now - lastGood.Value;

        if (age < TimeSpan.Zero)
        {
            return "Hace un momento";
        }

        if (age.TotalMinutes < 90)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Hace {Math.Max(1, (int)age.TotalMinutes)} min");
        }

        if (age.TotalHours < 48)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Hace {(int)age.TotalHours} h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"Hace {(int)age.TotalDays} días");
    }

    /// <summary>
    /// Los servidores de la lista de <c>NtpServer</c>.
    ///
    /// Vienen separados por espacios y cada uno lleva sus banderas detrás de una coma:
    /// <c>time.windows.com,0x9</c>. Un servidor sin coma es válido y significa banderas a
    /// cero, así que no se descarta.
    /// </summary>
    public static IReadOnlyList<NtpPeer> ParsePeers(string? ntpServer)
    {
        if (string.IsNullOrWhiteSpace(ntpServer))
        {
            return Array.Empty<NtpPeer>();
        }

        List<NtpPeer> peers = new();

        foreach (string chunk in ntpServer.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int comma = chunk.IndexOf(',', StringComparison.Ordinal);

            if (comma < 0)
            {
                peers.Add(new NtpPeer(chunk, 0));
                continue;
            }

            string host = chunk[..comma];
            string flags = chunk[(comma + 1)..].Trim();

            if (host.Length == 0)
            {
                continue;
            }

            peers.Add(new NtpPeer(host, ParseFlags(flags)));
        }

        return peers;
    }

    /// <summary>
    /// De dónde dice el sistema que saca la hora.
    ///
    /// No lleva objetivo a propósito. En un rig la respuesta correcta depende del montaje:
    /// un NTP de internet si hay salida, uno del switch de FOH si la red está aislada, o el
    /// reloj local si quien manda es otra cosa. Lo que hace falta es verlo, no juzgarlo.
    /// </summary>
    public static string DescribeSource(string? type, string? ntpServer)
    {
        IReadOnlyList<NtpPeer> peers = ParsePeers(ntpServer);

        string origin = string.Join(", ", peers.Select(p => p.Host));

        return type?.ToUpperInvariant() switch
        {
            "NTP" when peers.Count > 0 => origin,
            "NTP" => "NTP sin servidor configurado",
            "NT5DS" => "El dominio",
            "ALLSYNC" when peers.Count > 0 => $"Dominio o {origin}",
            "ALLSYNC" => "Dominio o NTP, lo que conteste",
            "NOSYNC" => "Nada: el reloj va solo",
            _ => "Sin determinar",
        };
    }

    private static uint ParseFlags(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text[2..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint hex)
                ? hex
                : 0;
        }

        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out uint plain)
            ? plain
            : 0;
    }
}
