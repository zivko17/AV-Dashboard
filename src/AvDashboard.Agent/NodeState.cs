using System.Collections.Concurrent;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Agent;

/// <summary>
/// Ultimo estado conocido de cada modulo, en memoria.
///
/// Los colectores escriben aqui y los endpoints leen de aqui. Es lo que permite que
/// `GET /api/v1/system` responda en microsegundos sin volver a consultar WMI, y lo que
/// mantiene el coste del agente donde tiene que estar.
/// </summary>
public sealed class NodeState
{
    private readonly ConcurrentDictionary<string, PingStatistics> _pingStats = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<PingSample>> _pingWindows =
        new(StringComparer.Ordinal);

    public SystemStatus? System { get; set; }
    public SystemSettings? SystemSettings { get; set; }
    public IReadOnlyList<AudioDevice> AudioDevices { get; set; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AsioDriver> AsioDrivers { get; set; } = Array.Empty<AsioDriver>();
    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; set; } = Array.Empty<NetworkInterfaceInfo>();
    public IReadOnlyList<ServiceInfo> Services { get; set; } = Array.Empty<ServiceInfo>();
    public IReadOnlyList<StorageVolume> Volumes { get; set; } = Array.Empty<StorageVolume>();
    public DisplayTopology? Displays { get; set; }
    public PowerStatus? Power { get; set; }

    public string? ActiveProfile { get; set; }
    public DateTimeOffset? ServiceBaselineTakenAt { get; set; }
    public IReadOnlyList<ServiceInfo> ServiceBaseline { get; set; } = Array.Empty<ServiceInfo>();

    public IReadOnlyDictionary<string, PingStatistics> PingStatistics => _pingStats;

    /// <summary>
    /// Ventana en vivo de muestras por dispositivo. Vive en memoria y no toca disco:
    /// el grafico del dashboard se refresca cada segundo y no puede pagar una consulta
    /// a SQLite cada vez.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PingSample>> PingWindows => _pingWindows;

    public void UpdatePing(string deviceId, PingStatistics statistics, IReadOnlyList<PingSample>? window = null)
    {
        _pingStats[deviceId] = statistics;

        if (window is not null)
        {
            _pingWindows[deviceId] = window;
        }
    }

    /// <summary>
    /// Salud por sector. Es exactamente lo que pinta el dashboard: un indicador por
    /// tarjeta y nada mas. Si hace falta leer un parrafo, la tarjeta esta mal disenada.
    /// </summary>
    public IReadOnlyDictionary<Sector, HealthState> EvaluateSectors()
    {
        Dictionary<Sector, HealthState> health = new();

        health[Sector.Audio] = AudioDiagnostics.Evaluate(AudioDevices);

        health[Sector.Network] = Interfaces.Count == 0
            ? HealthState.Unknown
            : new[]
            {
                Interfaces.Any(i => i.IsUp) ? HealthState.Ok : HealthState.Critical,
                Interfaces.Any(i => i.IsUp && i.PowerManagementEnabled == true)
                    ? HealthState.Warning
                    : HealthState.Ok,
                _pingStats.Values.Select(PingStatisticsCalculator.Evaluate).Worst(),
            }.Worst();

        health[Sector.Production] = Services.Count == 0
            ? HealthState.Unknown
            : Services.Any(s => s.IsUnexpectedlyStopped) ? HealthState.Critical : HealthState.Ok;

        health[Sector.SystemEssential] = System switch
        {
            null => HealthState.Unknown,
            { CpuTotalPercent: > 95 } => HealthState.Warning,
            { MemoryUsedPercent: > 92 } => HealthState.Warning,
            _ => HealthState.Ok,
        };

        health[Sector.Storage] = Volumes.Count == 0
            ? HealthState.Unknown
            : Volumes.Select(v => v.FreePercent switch
            {
                < 5 => HealthState.Critical,
                < 15 => HealthState.Warning,
                _ => HealthState.Ok,
            }).Worst();

        health[Sector.Video] = Displays switch
        {
            null => HealthState.Unknown,
            { ActiveCount: 0 } => HealthState.Critical,
            _ => HealthState.Ok,
        };

        health[Sector.Power] = Power switch
        {
            null or { UpsPresent: false, BatteryPresent: false } => HealthState.Unknown,
            { OnBattery: true } => HealthState.Critical,
            _ => HealthState.Ok,
        };

        return health;
    }

    public HealthState Overall() => EvaluateSectors().Values.Worst();
}
