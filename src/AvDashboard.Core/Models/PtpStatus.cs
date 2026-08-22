namespace AvDashboard.Core.Models;

public enum PtpVersion
{
    Unknown = 0,

    /// <summary>PTPv1 (IEEE 1588-2002). Lo que habla Dante nativo.</summary>
    V1 = 1,

    /// <summary>PTPv2 (IEEE 1588-2008). Lo que habla AES67.</summary>
    V2 = 2,
}

/// <summary>
/// Estado del reloj del segmento (M3-PTP). Requiere Npcap: PTP no se puede leer
/// con sockets normales. Si falta, este modelo llega con <see cref="Available"/> en false.
/// </summary>
public sealed record PtpStatus
{
    public bool Available { get; init; }

    /// <summary>Motivo por el que no hay datos: sin Npcap, sin trafico PTP en el segmento...</summary>
    public string? UnavailableReason { get; init; }

    public PtpVersion Version { get; init; } = PtpVersion.Unknown;

    /// <summary>Clock identity del Grandmaster actual, en formato EUI-64.</summary>
    public string? GrandmasterId { get; init; }

    /// <summary>Nombre amigable si se ha podido resolver por OUI o por alias del usuario.</summary>
    public string? GrandmasterName { get; init; }

    public byte? Priority1 { get; init; }
    public byte? Priority2 { get; init; }
    public byte? ClockClass { get; init; }
    public byte? ClockAccuracy { get; init; }

    /// <summary>Offset del reloj local respecto al Grandmaster, en nanosegundos.</summary>
    public double? OffsetFromMasterNs { get; init; }

    /// <summary>Deriva observada en la ventana de muestreo, en nanosegundos por segundo.</summary>
    public double? DriftNsPerSecond { get; init; }

    public int SyncLossCount { get; init; }

    /// <summary>
    /// Numero de cambios de Grandmaster observados. Cualquier valor distinto de cero
    /// durante un show significa normalmente que alguien ha enchufado algo.
    /// </summary>
    public int GrandmasterChangeCount { get; init; }

    public DateTimeOffset? LastGrandmasterChange { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Estado de Dante Virtual Soundcard (M3-DVS). DVS no publica API documentada:
/// lo que se sabe viene del servicio, su log y su presencia en la red Dante por mDNS.
/// Lo que no se puede leer se marca como no legible en vez de inventarse.
/// </summary>
public sealed record DvsStatus
{
    public bool Installed { get; init; }
    public bool ServiceRunning { get; init; }
    public string? Version { get; init; }

    /// <summary>Sample rate configurado, si el log o el registro lo revelan.</summary>
    public int? SampleRate { get; init; }

    /// <summary>Latencia configurada en milisegundos, si es legible.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>true si el nodo aparece anunciado en la red Dante por mDNS.</summary>
    public bool? VisibleOnDanteNetwork { get; init; }

    /// <summary>Campos que esta version no ha podido leer, para que la UI lo diga.</summary>
    public IReadOnlyList<string> UnreadableFields { get; init; } = Array.Empty<string>();
}
