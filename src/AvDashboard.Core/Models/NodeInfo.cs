namespace AvDashboard.Core.Models;

/// <summary>Identidad y estado basico de un nodo con agente.</summary>
public sealed record NodeInfo
{
    public required string Id { get; init; }
    public required string Hostname { get; init; }
    public string? LanIp { get; init; }
    public string? TailscaleIp { get; init; }
    public required string AgentVersion { get; init; }
    public required string OsVersion { get; init; }
    public TimeSpan Uptime { get; init; }
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>true cuando el agente corre como servicio; false en modo portatil.</summary>
    public bool IsService { get; init; }

    /// <summary>Nombre del perfil de escenario aplicado por ultima vez, si lo hay.</summary>
    public string? ActiveProfile { get; init; }
}
