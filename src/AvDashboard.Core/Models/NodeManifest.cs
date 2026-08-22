namespace AvDashboard.Core.Models;

/// <summary>
/// Manifiesto completo de un nodo (M17). Cuando el rig A funciona y el rig B no,
/// la respuesta suele estar en un diff de versiones.
/// </summary>
public sealed record NodeManifest
{
    public required string NodeId { get; init; }
    public required string Hostname { get; init; }
    public DateTimeOffset TakenAt { get; init; } = DateTimeOffset.UtcNow;

    public HardwareManifest Hardware { get; init; } = new();
    public OsManifest Os { get; init; } = new();

    /// <summary>Versiones de drivers y SDK relevantes, indexadas por nombre.</summary>
    public IReadOnlyDictionary<string, string> Drivers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Versiones de aplicaciones de produccion instaladas.</summary>
    public IReadOnlyDictionary<string, string> Applications { get; init; } =
        new Dictionary<string, string>();
}

public sealed record HardwareManifest
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }

    /// <summary>Numero de serie. Imprescindible para el inventario de flota.</summary>
    public string? SerialNumber { get; init; }

    public string? BiosVersion { get; init; }
    public string? Cpu { get; init; }
    public int LogicalCores { get; init; }
    public long TotalMemoryBytes { get; init; }
    public IReadOnlyList<string> Gpus { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Nics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Disks { get; init; } = Array.Empty<string>();
}

public sealed record OsManifest
{
    public string? Caption { get; init; }
    public string? Version { get; init; }
    public string? Build { get; init; }
    public string? DisplayVersion { get; init; }
    public DateTimeOffset? InstallDate { get; init; }
    public IReadOnlyList<string> RecentHotfixes { get; init; } = Array.Empty<string>();
}

/// <summary>Diferencia de un campo del manifiesto entre dos nodos.</summary>
public sealed record ManifestDrift
{
    public required string Path { get; init; }
    public required string LeftNode { get; init; }
    public required string RightNode { get; init; }
    public string? LeftValue { get; init; }
    public string? RightValue { get; init; }
}
