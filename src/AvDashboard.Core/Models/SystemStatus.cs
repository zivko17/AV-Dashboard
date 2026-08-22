namespace AvDashboard.Core.Models;

/// <summary>Instantanea de telemetria de sistema. Se emite una vez por segundo (M1).</summary>
public sealed record SystemStatus
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public double CpuTotalPercent { get; init; }

    /// <summary>Uso por core logico. Vacio si el proveedor no lo expone.</summary>
    public IReadOnlyList<double> CpuPerCorePercent { get; init; } = Array.Empty<double>();

    public double MemoryUsedPercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long MemoryTotalBytes { get; init; }

    /// <summary>Temperatura de CPU en grados Celsius. Null si no hay sensor legible.</summary>
    public double? CpuTemperatureC { get; init; }

    public double? GpuTemperatureC { get; init; }

    /// <summary>
    /// Eventos de throttling termico o por limite de potencia desde el arranque.
    /// Un contador que sube durante un show explica los frames perdidos.
    /// </summary>
    public long ThermalThrottleEvents { get; init; }

    public long PowerLimitThrottleEvents { get; init; }

    public TimeSpan Uptime { get; init; }
}

/// <summary>Ajustes de sistema que el modulo M1 lee y (a partir de la Fase 2) escribe.</summary>
public sealed record SystemSettings
{
    public string? ActivePowerPlanName { get; init; }
    public Guid? ActivePowerPlanGuid { get; init; }

    /// <summary>true si el esquema Ultimate Performance esta duplicado y disponible.</summary>
    public bool UltimatePerformanceAvailable { get; init; }

    public int? ProcessorMinStatePercent { get; init; }
    public int? ProcessorMaxStatePercent { get; init; }
    public bool? CoreParkingEnabled { get; init; }

    /// <summary>HiberbootEnabled. Critico: con Fast Startup un rig no arranca limpio tras un corte.</summary>
    public bool? FastStartupEnabled { get; init; }

    public bool? UsbSelectiveSuspendEnabled { get; init; }

    /// <summary>SystemResponsiveness de MMCSS. 20 es el valor de Windows; 10 o 0 para AV.</summary>
    public int? MmcssSystemResponsiveness { get; init; }

    /// <summary>NetworkThrottlingIndex. 0xFFFFFFFF desactiva el throttling: lo que quiere NDI y DVS.</summary>
    public uint? NetworkThrottlingIndex { get; init; }

    public VisualEffectsSetting VisualEffects { get; init; } = VisualEffectsSetting.Unknown;

    public bool? GameBarEnabled { get; init; }
    public bool? HardwareAcceleratedGpuScheduling { get; init; }
    public FocusAssistMode FocusAssist { get; init; } = FocusAssistMode.Unknown;

    public WindowsUpdateStatus WindowsUpdate { get; init; } = new();

    /// <summary>
    /// Estado de Defender. Solo lectura: con Tamper Protection activo no es
    /// desactivable por software, y el modulo lo dice en vez de fingir que puede.
    /// </summary>
    public DefenderStatus Defender { get; init; } = new();
}

public enum VisualEffectsSetting
{
    Unknown = 0,
    LetWindowsChoose = 1,
    BestAppearance = 2,
    BestPerformance = 3,
    Custom = 4,
}

public enum FocusAssistMode
{
    Unknown = 0,
    Off = 1,
    PriorityOnly = 2,
    AlarmsOnly = 3,
}

public sealed record WindowsUpdateStatus
{
    public bool? PausedUntilKnown { get; init; }
    public DateTimeOffset? PausedUntil { get; init; }
    public TimeSpan? ActiveHoursStart { get; init; }
    public TimeSpan? ActiveHoursEnd { get; init; }
    public bool? AutoRestartBlocked { get; init; }
    public bool RebootPending { get; init; }
}

public sealed record DefenderStatus
{
    public bool? RealTimeProtectionEnabled { get; init; }
    public bool? TamperProtectionEnabled { get; init; }

    /// <summary>
    /// Cuando es true el toggle desde la app es imposible y la UI debe decirlo
    /// claramente en vez de ofrecer un interruptor que no hace nada.
    /// </summary>
    public bool RequiresManualIntervention => TamperProtectionEnabled == true;
}

/// <summary>Prioridad y afinidad persistentes por proceso (M1).</summary>
public sealed record ProcessPolicy
{
    public required string ImageName { get; init; }
    public ProcessPriority Priority { get; init; } = ProcessPriority.Normal;

    /// <summary>Mascara de afinidad. Null = sin fijar.</summary>
    public ulong? AffinityMask { get; init; }
}

public enum ProcessPriority
{
    Idle = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    Realtime = 5,
}
