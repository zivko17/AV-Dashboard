namespace AvDashboard.Core.Models;

/// <summary>
/// Instantanea persistida del estado de los modulos que saben restaurarse.
/// Ningun cambio sin snapshot previo: es la precondicion de todo el modulo de perfiles.
/// </summary>
public sealed record Snapshot
{
    public required string Id { get; init; }
    public required string NodeId { get; init; }
    public required DateTimeOffset TakenAt { get; init; }

    /// <summary>Que lo provoco: "profile:evento-rig", "manual", "pre-apply".</summary>
    public required string Trigger { get; init; }

    /// <summary>Estado por modulo, indexado por ModuleId. Cada modulo define su formato.</summary>
    public IReadOnlyDictionary<string, string> Payload { get; init; } =
        new Dictionary<string, string>();

    public string? Notes { get; init; }
}
