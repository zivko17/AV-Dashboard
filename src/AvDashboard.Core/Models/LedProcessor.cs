namespace AvDashboard.Core.Models;

/// <summary>
/// Capa de informacion disponible para un procesador LED (M4-LED).
/// Novastar no publica API abierta documentada: el diseno es por capas y la capa 1
/// es la unica que se puede prometer hoy.
/// </summary>
public enum LedProbeLayer
{
    /// <summary>Ping, TCP, sondeo de puerto web, deteccion de reinicio. Funciona siempre.</summary>
    Reachability = 1,

    /// <summary>Lectura de la interfaz web del equipo (HTML o JSON).</summary>
    WebScraping = 2,

    /// <summary>Protocolo reconstruido por ingenieria inversa. Solo con hardware delante.</summary>
    ReverseEngineeredProtocol = 3,
}

public sealed record LedProcessor
{
    public required string Id { get; init; }

    /// <summary>Alias del usuario: "Novastar VX600 rack B", no "192.168.0.47".</summary>
    public required string Alias { get; init; }

    public required string Address { get; init; }
    public string? Model { get; init; }

    public LedProbeLayer AchievedLayer { get; init; } = LedProbeLayer.Reachability;

    // --- Capa 1: siempre disponible ---
    public bool Reachable { get; init; }
    public double? RttMs { get; init; }
    public int? WebPort { get; init; }
    public bool WebPortOpen { get; init; }

    /// <summary>Uptime reportado; un salto hacia atras es un reinicio que nadie ha anunciado.</summary>
    public TimeSpan? Uptime { get; init; }

    public DateTimeOffset? LastRebootDetected { get; init; }

    // --- Capas 2 y 3: pueden venir vacias y eso es correcto ---
    public double? TemperatureC { get; init; }
    public IReadOnlyList<LedOutputPort> OutputPorts { get; init; } = Array.Empty<LedOutputPort>();
    public bool? RedundancyActive { get; init; }
    public bool? GenlockLocked { get; init; }
    public int? ActiveMappingId { get; init; }

    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LedOutputPort
{
    public int Index { get; init; }
    public bool LinkUp { get; init; }
    public string? Description { get; init; }
}
