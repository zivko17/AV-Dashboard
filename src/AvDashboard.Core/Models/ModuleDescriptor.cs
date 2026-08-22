namespace AvDashboard.Core.Models;

/// <summary>
/// Por que un modulo esta o no esta disponible en este nodo. La consola pinta en
/// gris lo que no aplica en vez de mentir con un cero.
/// </summary>
public enum ModuleAvailability
{
    Available,

    /// <summary>Falta el hardware (no hay DeckLink, no hay UPS).</summary>
    MissingHardware,

    /// <summary>Falta una dependencia externa (Npcap, SDK de NDI, Desktop Video).</summary>
    MissingDependency,

    /// <summary>Existe pero requiere privilegios que el proceso actual no tiene.</summary>
    InsufficientPrivileges,

    /// <summary>Apagado a mano en la configuracion del nodo.</summary>
    DisabledByConfiguration,

    /// <summary>Aun no implementado en esta version del agente.</summary>
    NotImplemented,
}

public sealed record ModuleDescriptor
{
    /// <summary>Identificador estable del modulo: "M1", "M3", "M4-LED"...</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ModuleAvailability Availability { get; init; }

    /// <summary>Explicacion legible de por que no esta disponible. Null si lo esta.</summary>
    public string? Reason { get; init; }

    public bool IsAvailable => Availability == ModuleAvailability.Available;
}
