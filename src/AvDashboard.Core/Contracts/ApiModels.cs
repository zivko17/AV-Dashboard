using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Core.Contracts;

/// <summary>Respuesta de <c>GET /api/v1/node</c>.</summary>
public sealed record NodeResponse
{
    public required NodeInfo Node { get; init; }
    public required HealthState Health { get; init; }
    public int ActiveAlerts { get; init; }
}

/// <summary>Respuesta de <c>GET /api/v1/system</c>.</summary>
public sealed record SystemResponse
{
    public required SystemStatus Status { get; init; }
    public required SystemSettings Settings { get; init; }
}

/// <summary>Respuesta de <c>GET /api/v1/audio/devices</c>.</summary>
public sealed record AudioResponse
{
    public IReadOnlyList<AudioDevice> Devices { get; init; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AsioDriver> AsioDrivers { get; init; } = Array.Empty<AsioDriver>();
    public required HealthState Health { get; init; }

    /// <summary>Endpoints con el sample rate descuadrado, ya resueltos para la UI.</summary>
    public IReadOnlyList<string> SampleRateMismatches { get; init; } = Array.Empty<string>();
}

/// <summary>Respuesta de <c>GET /api/v1/network/interfaces</c>.</summary>
public sealed record NetworkResponse
{
    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; init; } = Array.Empty<NetworkInterfaceInfo>();
    public IReadOnlyList<NetworkProfile> Profiles { get; init; } = Array.Empty<NetworkProfile>();
    public required HealthState Health { get; init; }
}

/// <summary>Respuesta de <c>GET /api/v1/services</c>, ya agrupada por sector.</summary>
public sealed record ServicesResponse
{
    public IReadOnlyList<ServiceSectorGroup> Sectors { get; init; } = Array.Empty<ServiceSectorGroup>();
    public required HealthState Health { get; init; }
    public DateTimeOffset? BaselineTakenAt { get; init; }
}

public sealed record ServiceSectorGroup
{
    public required Sector Sector { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<ServiceInfo> Services { get; init; } = Array.Empty<ServiceInfo>();

    /// <summary>Servicios esperados que estan corriendo, sobre el total de esperados.</summary>
    public int RunningExpected => Services.Count(s => s.ExpectedRunning && s.State == ServiceState.Running);
    public int TotalExpected => Services.Count(s => s.ExpectedRunning);

    public HealthState Health => TotalExpected == 0
        ? HealthState.Ok
        : RunningExpected == TotalExpected ? HealthState.Ok : HealthState.Warning;
}

/// <summary>Peticion de escaneo de un segmento.</summary>
public sealed record ScanRequest
{
    /// <summary>Rango en CIDR. Null = se deduce de la interfaz indicada.</summary>
    public string? Cidr { get; init; }

    public string? InterfaceId { get; init; }
    public bool IncludeMdns { get; init; } = true;
    public bool IncludePortProbe { get; init; } = true;
    public int TimeoutMs { get; init; } = 800;
}

/// <summary>
/// Cuerpo de <c>POST /api/v1/network/devices</c>: como queda un equipo del inventario.
///
/// Un solo contrato para alta manual, renombrar, marcar y desmarcar, porque las cuatro son
/// la misma frase. Los campos son opcionales para que marcar una estrella no obligue a
/// reenviar el alias y arriesgarse a borrarlo por olvido.
/// </summary>
public sealed record DeviceUpsertRequest
{
    /// <summary>Direccion IP. Es la identidad del equipo en el inventario.</summary>
    public required string IpAddress { get; init; }

    /// <summary>Nombre que le pones tu. Cadena vacia lo borra; null lo deja como estaba.</summary>
    public string? Alias { get; init; }

    /// <summary>Si entra en el ping continuo. Null lo deja como estaba.</summary>
    public bool? IsCritical { get; init; }

    public DeviceKind? Kind { get; init; }
    public string? Notes { get; init; }
}

public sealed record ScanResponse
{
    public required DateTimeOffset StartedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string Range { get; init; }
    public IReadOnlyList<DiscoveredDevice> Devices { get; init; } = Array.Empty<DiscoveredDevice>();
}

/// <summary>Respuesta de <c>GET /api/v1/timeline</c>.</summary>
public sealed record TimelineResponse
{
    public IReadOnlyList<TimelineEvent> Events { get; init; } = Array.Empty<TimelineEvent>();
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>Error uniforme. El agente nunca devuelve un stack trace a la consola.</summary>
public sealed record ApiError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }

    public static ApiError ModuleUnavailable(string moduleId, string reason) => new()
    {
        Code = "module_unavailable",
        Message = $"El módulo {moduleId} no está disponible en este nodo.",
        Detail = reason,
    };

    public static ApiError NotImplemented(string what) => new()
    {
        Code = "not_implemented",
        Message = $"{what} todavía no está implementado en esta versión del agente.",
    };
}

/// <summary>Los perfiles que conoce el nodo: los compilados y los guardados.</summary>
public sealed record ProfilesResponse
{
    public IReadOnlyList<AvDashboard.Core.Profiles.ProfileDefinition> BuiltIn { get; init; } =
        Array.Empty<AvDashboard.Core.Profiles.ProfileDefinition>();

    public IReadOnlyList<AvDashboard.Core.Profiles.ProfileDefinition> Stored { get; init; } =
        Array.Empty<AvDashboard.Core.Profiles.ProfileDefinition>();
}

/// <summary>Resultado de importar un perfil de un fichero.</summary>
public sealed record ProfileImportResponse
{
    public required bool Saved { get; init; }
    public string? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public string? Error { get; init; }

    /// <summary>Actuadores que el perfil referencia y este nodo no conoce.</summary>
    public IReadOnlyList<string> UnknownActions { get; init; } = Array.Empty<string>();
}
