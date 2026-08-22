using AvDashboard.Core.Models;

namespace AvDashboard.Core.Contracts;

/// <summary>
/// Nombres de los mensajes que emite el hub de telemetria. La consola se suscribe
/// solo a los que la vista activa necesita: enviar medidores de audio a 20 fps a una
/// consola que esta mirando la pestana de Red es tirar CPU en la maquina que
/// intentamos optimizar.
/// </summary>
public static class TelemetryChannels
{
    /// <summary>Cadencia 1 s. Siempre activo.</summary>
    public const string SystemStatus = "system";

    /// <summary>Cadencia 20 fps. Solo con la vista de Audio abierta.</summary>
    public const string AudioMeters = "audio.meters";

    /// <summary>Cadencia 1 s.</summary>
    public const string NetworkThroughput = "network.throughput";

    /// <summary>Cadencia de la sonda de ping, tipicamente 1 s.</summary>
    public const string PingSamples = "network.ping";

    /// <summary>Eventos del Timeline segun se producen.</summary>
    public const string TimelineEvents = "timeline";

    /// <summary>Alta y baja de alertas.</summary>
    public const string Alerts = "alerts";

    /// <summary>Resumen de salud del nodo. Lo que pinta el semaforo de la barra superior.</summary>
    public const string NodeHealth = "node.health";
}

/// <summary>Trama de telemetria de sistema enviada por el hub.</summary>
public sealed record SystemTelemetryFrame
{
    public required string NodeId { get; init; }
    public required SystemStatus Status { get; init; }
}

/// <summary>Medidores de audio. Se envian en bloque para no generar un mensaje por sesion.</summary>
public sealed record AudioMeterFrame
{
    public required string NodeId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<AudioMeterReading> Readings { get; init; } = Array.Empty<AudioMeterReading>();
}

public readonly record struct AudioMeterReading
{
    public required string Id { get; init; }
    public required float Peak { get; init; }
    public bool IsSession { get; init; }
}

public sealed record PingFrame
{
    public required string NodeId { get; init; }
    public required string DeviceId { get; init; }
    public required string Target { get; init; }
    public required PingSample Sample { get; init; }
}

public sealed record ThroughputFrame
{
    public required string NodeId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<InterfaceThroughput> Interfaces { get; init; } = Array.Empty<InterfaceThroughput>();
}

public readonly record struct InterfaceThroughput
{
    public required string InterfaceId { get; init; }
    public required double SendMbps { get; init; }
    public required double ReceiveMbps { get; init; }
    public double MulticastMbps { get; init; }
    public double MulticastPps { get; init; }
}

/// <summary>Resumen de salud por sector: lo que pinta el dashboard sin pedir nada mas.</summary>
public sealed record NodeHealthFrame
{
    public required string NodeId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required HealthState Overall { get; init; }
    public IReadOnlyDictionary<string, HealthState> BySector { get; init; } =
        new Dictionary<string, HealthState>();
    public int ActiveAlerts { get; init; }
}
