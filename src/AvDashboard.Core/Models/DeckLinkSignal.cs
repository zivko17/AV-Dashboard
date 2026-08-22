namespace AvDashboard.Core.Models;

/// <summary>Estado de un dispositivo Blackmagic DeckLink (M4).</summary>
public sealed record DeckLinkSignal
{
    public required string DeviceName { get; init; }
    public int DeviceIndex { get; init; }

    public bool SignalDetected { get; init; }

    /// <summary>Modo de video detectado: "1080p5994", "2160p25"...</summary>
    public string? DetectedMode { get; init; }

    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }
    public bool? Interlaced { get; init; }

    public string? PixelFormat { get; init; }

    /// <summary>
    /// Genlock enganchado. Sin esto, en un rig con varias entradas, los frames se
    /// van a deslizar y nadie sabra por que.
    /// </summary>
    public bool? GenlockLocked { get; init; }

    public string? ReferenceSource { get; init; }
    public string? HeldByProcess { get; init; }
}

/// <summary>Dispositivo de captura DirectShow o Media Foundation.</summary>
public sealed record CaptureDevice
{
    public required string Name { get; init; }
    public required string Framework { get; init; }
    public string? SymbolicLink { get; init; }
    public string? HeldByProcess { get; init; }
    public bool InUse => HeldByProcess is not null;
}

/// <summary>Fuente NDI descubierta en la red.</summary>
public sealed record NdiSource
{
    public required string Name { get; init; }
    public string? MachineName { get; init; }
    public string? Address { get; init; }
}

/// <summary>Estado de la GPU (M4).</summary>
public sealed record GpuStatus
{
    public required string Name { get; init; }
    public string? DriverVersion { get; init; }

    /// <summary>Rama del driver: Studio o Game Ready. No es lo mismo en produccion.</summary>
    public string? DriverBranch { get; init; }

    public double? Utilization3DPercent { get; init; }
    public double? NvencPercent { get; init; }
    public double? NvdecPercent { get; init; }
    public double? TemperatureC { get; init; }
    public long? MemoryUsedBytes { get; init; }
    public long? MemoryTotalBytes { get; init; }
}

/// <summary>
/// Desviacion entre el refresh real de la salida y el framerate de render que
/// reporta la aplicacion de produccion. No es un detector de tearing: es la medida
/// de la causa, que es lo unico que se puede medir con fiabilidad desde software.
/// </summary>
public sealed record RefreshSyncStatus
{
    public required string DisplayDeviceName { get; init; }
    public double DisplayRefreshHz { get; init; }

    /// <summary>Framerate reportado por vMix, Resolume o Watchout. Null si no hay app conectada.</summary>
    public double? RenderFrameRate { get; init; }

    public string? RenderSource { get; init; }

    /// <summary>Desviacion por frame en milisegundos.</summary>
    public double? DeviationMs =>
        RenderFrameRate is double render && render > 0 && DisplayRefreshHz > 0
            ? Math.Abs((1000.0 / DisplayRefreshHz) - (1000.0 / render))
            : null;

    /// <summary>Deriva acumulada en un minuto de show, en milisegundos.</summary>
    public double? DriftPerMinuteMs =>
        DeviationMs is double dev && RenderFrameRate is double render ? dev * render * 60 : null;
}
