namespace AvDashboard.Core.Models;

/// <summary>Topologia completa de salida de video de un nodo (M4).</summary>
public sealed record DisplayTopology
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DisplayOutput> Outputs { get; init; } = Array.Empty<DisplayOutput>();

    public int ActiveCount => Outputs.Count(o => o.IsActive);
}

public sealed record DisplayOutput
{
    /// <summary>Nombre de dispositivo de Windows: "\\\\.\\DISPLAY1".</summary>
    public required string DeviceName { get; init; }

    /// <summary>Nombre del monitor tal y como lo reporta el EDID o el driver.</summary>
    public string? MonitorName { get; init; }

    /// <summary>Salida fisica de la GPU: HDMI 1, DP 3... Requiere NVAPI para ser fiable.</summary>
    public string? PhysicalConnector { get; init; }

    public bool IsActive { get; init; }
    public bool IsPrimary { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Posicion del escritorio virtual. Es lo que Windows reordena cuando algo se cae.</summary>
    public int PositionX { get; init; }
    public int PositionY { get; init; }

    /// <summary>Refresh nominal en Hz segun el modo de display.</summary>
    public double RefreshHz { get; init; }

    /// <summary>
    /// Refresh medido via DwmGetCompositionTimingInfo. Es el que importa cuando el
    /// procesador LED va a 50.000 y el render a 59.940.
    /// </summary>
    public double? MeasuredRefreshHz { get; init; }

    public bool HdrEnabled { get; init; }
    public string? EdidManufacturerId { get; init; }
    public string? EdidSerial { get; init; }

    /// <summary>Hash del EDID leido, para el diff contra la referencia guardada (M18).</summary>
    public string? EdidHash { get; init; }
}

/// <summary>Layout de pantallas guardado, restaurable de un clic tras un reinicio del procesador LED.</summary>
public sealed record DisplayLayout
{
    public required string Name { get; init; }
    public required string NodeId { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
    public required DisplayTopology Topology { get; init; }
}
