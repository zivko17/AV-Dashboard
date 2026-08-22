using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Core.Models;

public enum Severity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

/// <summary>
/// Entrada del Show Timeline (M11): el log unificado y correlado por timestamp de
/// todos los modulos. Es lo que permite reconstruir que paso en vez de discutirlo.
/// </summary>
public sealed record TimelineEvent
{
    public long Id { get; init; }
    public required string NodeId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Modulo emisor: "M1", "M4", "M5"...</summary>
    public required string Module { get; init; }

    public Sector Sector { get; init; } = Sector.Unclassified;
    public Severity Severity { get; init; } = Severity.Info;

    /// <summary>Tipo estable y consultable: "display.lost", "ping.spike", "service.stopped".</summary>
    public required string Kind { get; init; }

    public required string Message { get; init; }

    /// <summary>Datos estructurados del evento, serializados. Lo que permite el drill-down.</summary>
    public string? PayloadJson { get; init; }
}

/// <summary>Alerta activa. A diferencia de un evento, tiene ciclo de vida y se reconoce.</summary>
public sealed record Alert
{
    public long Id { get; init; }
    public required string NodeId { get; init; }
    public required DateTimeOffset RaisedAt { get; init; }
    public DateTimeOffset? ClearedAt { get; init; }
    public required string Module { get; init; }
    public Sector Sector { get; init; } = Sector.Unclassified;
    public Severity Severity { get; init; } = Severity.Warning;
    public required string Kind { get; init; }
    public required string Message { get; init; }
    public bool Acknowledged { get; init; }

    public bool IsActive => ClearedAt is null;
}
