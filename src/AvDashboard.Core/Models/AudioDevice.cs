namespace AvDashboard.Core.Models;

public enum AudioDataFlow
{
    Render,
    Capture,
}

public enum AudioDeviceState
{
    Unknown = 0,
    Active = 1,
    Disabled = 2,
    NotPresent = 3,
    Unplugged = 4,
}

/// <summary>Un endpoint de Core Audio con lo que hace falta saber antes de un show (M3).</summary>
public sealed record AudioDevice
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }

    /// <summary>Nombre del dispositivo fisico sin el sufijo del endpoint.</summary>
    public string? DeviceDescription { get; init; }

    public required AudioDataFlow DataFlow { get; init; }
    public AudioDeviceState State { get; init; } = AudioDeviceState.Unknown;

    public bool IsDefault { get; init; }
    public bool IsDefaultCommunications { get; init; }

    /// <summary>Formato de mezcla actual: lo que el motor de Windows esta usando de verdad.</summary>
    public int MixSampleRate { get; init; }
    public int MixBitDepth { get; init; }
    public int MixChannels { get; init; }

    /// <summary>Formato por defecto declarado en las propiedades del endpoint.</summary>
    public int? DefaultSampleRate { get; init; }
    public int? DefaultBitDepth { get; init; }

    public bool ExclusiveModeAllowed { get; init; }

    /// <summary>Nombre del proceso que tiene el dispositivo en modo exclusivo, si alguno.</summary>
    public string? ExclusiveHolder { get; init; }

    public float PeakLevel { get; init; }
    public float VolumeScalar { get; init; }
    public bool Muted { get; init; }

    /// <summary>
    /// Discrepancia entre el formato por defecto del endpoint y el formato de mezcla.
    /// Causa numero uno de crujidos, y no aparece en ningun sitio de la UI de Windows.
    /// </summary>
    public bool HasSampleRateMismatch =>
        DefaultSampleRate is int declared && MixSampleRate > 0 && declared != MixSampleRate;
}

/// <summary>Una sesion de audio por aplicacion, para la matriz de ruteo y los medidores.</summary>
public sealed record AudioSession
{
    public required string SessionId { get; init; }
    public int ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public string? DisplayName { get; init; }
    public required string DeviceId { get; init; }
    public float PeakLevel { get; init; }
    public float VolumeScalar { get; init; }
    public bool Muted { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Driver ASIO declarado en HKLM\SOFTWARE\ASIO.</summary>
public sealed record AsioDriver
{
    public required string Name { get; init; }
    public required string ClsId { get; init; }
    public string? Description { get; init; }
    public string? DriverPath { get; init; }

    /// <summary>Proceso que tiene el driver tomado, si se pudo determinar.</summary>
    public string? HeldBy { get; init; }
}
