namespace AvDashboard.Core.Models;

/// <summary>Unidad de almacenamiento (M9). Un disco lleno a mitad de grabacion es evitable.</summary>
public sealed record StorageVolume
{
    public required string DriveLetter { get; init; }
    public string? Label { get; init; }
    public string? FileSystem { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public double FreePercent => TotalBytes == 0 ? 0 : FreeBytes * 100.0 / TotalBytes;

    /// <summary>Velocidad de escritura secuencial medida bajo demanda, en MB/s.</summary>
    public double? MeasuredWriteMbps { get; init; }

    public string? SmartStatus { get; init; }

    /// <summary>Marcada como unidad de grabacion: entra en la checklist pre-show.</summary>
    public bool IsRecordingTarget { get; init; }
}

/// <summary>Estado de SAI o bateria de portatil (M14).</summary>
public sealed record PowerStatus
{
    public bool UpsPresent { get; init; }
    public bool? OnBattery { get; init; }
    public double? ChargePercent { get; init; }
    public TimeSpan? EstimatedRuntime { get; init; }
    public string? UpsModel { get; init; }
    public string? Source { get; init; }

    public bool BatteryPresent { get; init; }
    public double? BatteryChargePercent { get; init; }
    public bool? BatteryCharging { get; init; }
}

/// <summary>Estado de licencia o dongle de una aplicacion (M13).</summary>
public sealed record LicenseStatus
{
    public required string Product { get; init; }
    public bool? Licensed { get; init; }
    public string? LicenseType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>true si el producto depende de un dongle fisico y este esta presente.</summary>
    public bool? DonglePresent { get; init; }

    public string? Detail { get; init; }
}
