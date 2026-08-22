using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Core.Models;

public enum ServiceState
{
    Unknown = 0,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
}

public enum ServiceStartMode
{
    Unknown = 0,
    Boot,
    System,
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
}

/// <summary>Un servicio de Windows visto desde el angulo AV (M6).</summary>
public sealed record ServiceInfo
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public ServiceState State { get; init; } = ServiceState.Unknown;
    public ServiceStartMode StartMode { get; init; } = ServiceStartMode.Unknown;

    public string? Account { get; init; }
    public int? ProcessId { get; init; }
    public long? WorkingSetBytes { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();

    /// <summary>Sector AV al que pertenece. La clasificacion es editable y persistente.</summary>
    public Sector Sector { get; init; } = Sector.Unclassified;

    /// <summary>
    /// true cuando el perfil activo espera este servicio en marcha. Un servicio parado
    /// que debe estarlo no genera aviso, y esa es toda la gracia del contador por sector.
    /// </summary>
    public bool ExpectedRunning { get; init; }

    public bool IsUnexpectedlyStopped => ExpectedRunning && State != ServiceState.Running;
}

/// <summary>Diferencia de un servicio contra el baseline. La vista que abres cuando algo dejo de ir.</summary>
public sealed record ServiceDiffEntry
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required ServiceDiffKind Kind { get; init; }

    public ServiceState? BaselineState { get; init; }
    public ServiceState? CurrentState { get; init; }
    public ServiceStartMode? BaselineStartMode { get; init; }
    public ServiceStartMode? CurrentStartMode { get; init; }
    public Sector Sector { get; init; } = Sector.Unclassified;
}

public enum ServiceDiffKind
{
    StateChanged,
    StartModeChanged,
    Added,
    Removed,
}
