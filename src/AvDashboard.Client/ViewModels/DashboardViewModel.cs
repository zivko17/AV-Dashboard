using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// El dashboard responde a «¿va todo bien?» en menos de dos segundos. Todo lo demas es
/// drill-down: cada tarjeta navega a su seccion con el contexto ya filtrado.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<string> _navigate;
    private CancellationTokenSource? _autoRefresh;

    /// <summary>
    /// Cada cuanto se repinta el dashboard mientras se esta mirando. Cinco segundos: lo
    /// bastante vivo para que sirva de vigilancia y lo bastante lento para que no cueste.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    public DashboardViewModel(NodeManager nodes, IUiShell shell, Action<string> navigate)
        : base(nodes, shell)
    {
        _navigate = navigate;
    }

    public override async Task ActivateAsync()
    {
        await base.ActivateAsync().ConfigureAwait(true);

        _autoRefresh = new CancellationTokenSource();
        _ = AutoRefreshLoopAsync(_autoRefresh.Token);
    }

    public override Task DeactivateAsync()
    {
        // Sin esto, el dashboard seguiria pidiendo datos desde una pestana que nadie mira.
        _autoRefresh?.Cancel();
        _autoRefresh?.Dispose();
        _autoRefresh = null;

        return base.DeactivateAsync();
    }

    private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Se ha salido de la vista.
        }
    }

    public override string Title => "Dashboard";
    // Codepoints de Segoe Fluent Icons; si alguno sale como caja, se ajusta aqui.
    public override string Glyph => "\uE80F";

    public ObservableCollection<StatusCardViewModel> Cards { get; } = new();
    public ObservableCollection<Alert> ActiveAlerts { get; } = new();
    public ObservableCollection<PingSeriesViewModel> PingSeries { get; } = new();

    [ObservableProperty]
    private string _nodeSummary = "Conectando…";

    protected override async Task LoadAsync()
    {
        ApiClient api = Nodes.Api();

        ApiResult<NodeResponse> node = await api.GetNodeAsync().ConfigureAwait(true);
        if (!Apply(node, out NodeResponse nodeInfo))
        {
            NodeSummary = "El agente no responde.";
            Cards.Clear();
            return;
        }

        Health = nodeInfo.Health;
        NodeSummary = $"{nodeInfo.Node.Hostname} · agente {nodeInfo.Node.AgentVersion} · " +
                      $"activo desde hace {FormatUptime(nodeInfo.Node.Uptime)}";

        ApiResult<SystemResponse> system = await api.GetSystemAsync().ConfigureAwait(true);
        ApiResult<AudioResponse> audio = await api.GetAudioAsync().ConfigureAwait(true);
        ApiResult<DisplayTopology> displays = await api.GetDisplaysAsync().ConfigureAwait(true);
        ApiResult<NetworkResponse> network = await api.GetNetworkAsync().ConfigureAwait(true);
        ApiResult<ServicesResponse> services = await api.GetServicesAsync().ConfigureAwait(true);
        ApiResult<IReadOnlyList<StorageVolume>> storage = await api.GetStorageAsync().ConfigureAwait(true);
        ApiResult<IReadOnlyList<PingStatistics>> pings = await api.GetPingAsync().ConfigureAwait(true);
        ApiResult<IReadOnlyList<Alert>> alerts = await api.GetAlertsAsync().ConfigureAwait(true);
        ApiResult<IReadOnlyDictionary<string, IReadOnlyList<PingSample>>> windows =
            await api.GetPingWindowsAsync().ConfigureAwait(true);

        Cards.Clear();
        Cards.Add(BuildSystemCard(system.Value));
        Cards.Add(BuildAudioCard(audio.Value));
        Cards.Add(BuildVideoCard(displays.Value));
        Cards.Add(BuildNetworkCard(network.Value, pings.Value));
        Cards.Add(BuildServicesCard(services.Value));
        Cards.Add(BuildStorageCard(storage.Value));

        ActiveAlerts.Clear();
        foreach (Alert alert in alerts.Value ?? Array.Empty<Alert>())
        {
            ActiveAlerts.Add(alert);
        }

        PingSeries.Clear();
        foreach (PingStatistics stats in pings.Value ?? Array.Empty<PingStatistics>())
        {
            // Las estadisticas vienen etiquetadas con el alias y la ventana con la
            // direccion; se casan por la que coincida, y si no hay, la serie sale vacia.
            IReadOnlyList<PingSample>? window = windows.Value?
                .FirstOrDefault(w => w.Key == stats.Target ||
                                     stats.Target.Contains(w.Key, StringComparison.Ordinal)).Value;

            PingSeries.Add(new PingSeriesViewModel(stats, window));
        }
    }

    private StatusCardViewModel BuildSystemCard(SystemResponse? system) => new()
    {
        Title = "SISTEMA",
        Section = "Sistema",
        Navigate = _navigate,
        Health = system is null
            ? HealthState.Unknown
            : system.Status.CpuTotalPercent > 95 ? HealthState.Warning : HealthState.Ok,
        Primary = system is null ? "—" : $"CPU {system.Status.CpuTotalPercent:0}%",
        Lines = system is null
            ? new[] { "sin datos" }
            : new[]
            {
                $"RAM {system.Status.MemoryUsedPercent:0}%",
                system.Settings.ActivePowerPlanName ?? "plan de energía desconocido",
                system.Settings.FastStartupEnabled == true ? "⚠ Fast Startup activo" : "Fast Startup off",
            },
    };

    private StatusCardViewModel BuildAudioCard(AudioResponse? audio) => new()
    {
        Title = "AUDIO",
        Section = "Audio",
        Navigate = _navigate,
        Health = audio?.Health ?? HealthState.Unknown,
        Primary = audio?.Devices.FirstOrDefault(d => d.IsDefault && d.DataFlow == AudioDataFlow.Render) is AudioDevice def
            ? $"{Shorten(def.FriendlyName)} {def.MixSampleRate / 1000.0:0.#}k"
            : "sin salida por defecto",
        Lines = audio is null
            ? new[] { "sin datos" }
            : new[]
            {
                $"{audio.Devices.Count(d => d.State == AudioDeviceState.Active)} endpoints activos",
                audio.SampleRateMismatches.Count == 0
                    ? "sample rates alineados"
                    : $"⚠ {audio.SampleRateMismatches.Count} con mismatch",
                audio.AsioDrivers.Count == 0 ? "sin drivers ASIO" : $"{audio.AsioDrivers.Count} drivers ASIO",
            },
    };

    private StatusCardViewModel BuildNetworkCard(
        NetworkResponse? network,
        IReadOnlyList<PingStatistics>? pings)
    {
        NetworkInterfaceInfo? primary = PickPrimaryInterface(network?.Interfaces);

        double? worstRtt = pings?.Where(p => p.AverageMs is not null).Max(p => p.AverageMs);

        return new StatusCardViewModel
        {
            Title = "RED",
            Section = "Red",
            Navigate = _navigate,
            Health = network?.Health ?? HealthState.Unknown,
            Primary = primary is null ? "sin enlace" : $"{primary.Name} {primary.Ipv4Addresses[0]}",
            Lines = network is null
                ? new[] { "sin datos" }
                : new[]
                {
                    worstRtt is double rtt ? $"peor RTT {rtt:0.#} ms" : "sin ping continuo",
                    $"{network.Interfaces.Count(i => i.IsUp)} interfaces activas",
                    network.Interfaces.Any(i => i.IsUp && i.PowerManagementEnabled == true)
                        ? "⚠ power mgmt activo en una NIC"
                        : "power mgmt correcto",
                },
        };
    }

    /// <summary>
    /// La interfaz que la tarjeta debe enseñar.
    ///
    /// Coger la primera activa con IPv4 saca el túnel de Tailscale, que es justo la
    /// información equivocada: en un rig quieres ver la interfaz por la que va Dante, no
    /// la VPN. Tener puerta de enlace es lo que distingue a una NIC real de un túnel o de
    /// un adaptador host-only de VirtualBox.
    /// </summary>
    private static NetworkInterfaceInfo? PickPrimaryInterface(
        IReadOnlyList<NetworkInterfaceInfo>? interfaces)
    {
        if (interfaces is null)
        {
            return null;
        }

        List<NetworkInterfaceInfo> candidates = interfaces
            .Where(i => i.IsUp && i.Ipv4Addresses.Count > 0)
            .ToList();

        return candidates.FirstOrDefault(i => i.Gateways.Count > 0)
               ?? candidates.FirstOrDefault();
    }

    private StatusCardViewModel BuildServicesCard(ServicesResponse? services)
    {
        int expected = services?.Sectors.Sum(s => s.TotalExpected) ?? 0;
        int running = services?.Sectors.Sum(s => s.RunningExpected) ?? 0;

        return new StatusCardViewModel
        {
            Title = "SERVICIOS",
            Section = "Servicios",
            Navigate = _navigate,
            Health = services?.Health ?? HealthState.Unknown,
            Primary = services is null ? "—" : $"{running}/{expected} AV",
            Lines = services is null
                ? new[] { "sin datos" }
                : new[]
                {
                    $"{services.Sectors.Sum(s => s.Services.Count)} servicios",
                    services.BaselineTakenAt is DateTimeOffset taken
                        ? $"baseline {taken.LocalDateTime:dd/MM HH:mm}"
                        : "sin baseline tomado",
                },
        };
    }

    /// <summary>
    /// Vídeo en el dashboard.
    ///
    /// Lo primero es cuántas salidas hay activas, porque perder una en una pared LED
    /// significa haber perdido la pared. Debajo, el refresco de la principal escrito
    /// entero: es el dato que delata un procesador a 50 y un render a 59,94, y redondeado
    /// a «60 Hz» no delata nada.
    /// </summary>
    private StatusCardViewModel BuildVideoCard(DisplayTopology? topology)
    {
        if (topology is null || topology.Outputs.Count == 0)
        {
            return new StatusCardViewModel
            {
                Title = "VÍDEO",
                Health = HealthState.Unknown,
                Primary = "sin datos",
                Section = "Vídeo",
                Navigate = _navigate,
            };
        }

        DisplayOutput primary = topology.Outputs.FirstOrDefault(o => o.IsPrimary)
                                ?? topology.Outputs[0];

        List<string> lines = new()
        {
            primary.MonitorName ?? primary.DeviceName,
            $"{primary.Width}×{primary.Height} · {primary.RefreshHz:0.###} Hz",
        };

        string connectors = string.Join(", ", topology.Outputs
            .Where(o => o.IsActive && o.PhysicalConnector is not null)
            .Select(o => o.PhysicalConnector!)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        if (connectors.Length > 0)
        {
            lines.Add(connectors);
        }

        int withoutEdid = topology.Outputs.Count(o => o.IsActive && !o.EdidAvailable);

        if (withoutEdid > 0)
        {
            lines.Add($"⚠ {withoutEdid} sin EDID legible");
        }

        return new StatusCardViewModel
        {
            Title = "VÍDEO",
            // Cero salidas activas es rojo; que falte un EDID es ambar, no rojo: la imagen
            // sigue saliendo, pero no se puede comparar contra la referencia.
            Health = topology.ActiveCount == 0 ? HealthState.Critical
                : withoutEdid > 0 ? HealthState.Warning
                : HealthState.Ok,
            Primary = topology.ActiveCount == 1
                ? "1 salida activa"
                : $"{topology.ActiveCount} salidas activas",
            Lines = lines,
            Section = "Vídeo",
            Navigate = _navigate,
        };
    }

    private StatusCardViewModel BuildStorageCard(IReadOnlyList<StorageVolume>? volumes)
    {
        StorageVolume? tightest = volumes?.OrderBy(v => v.FreePercent).FirstOrDefault();

        return new StatusCardViewModel
        {
            Title = "ALMACENAMIENTO",
            Section = "Sistema",
            Navigate = _navigate,
            Health = tightest is null
                ? HealthState.Unknown
                : tightest.FreePercent switch
                {
                    < 5 => HealthState.Critical,
                    < 15 => HealthState.Warning,
                    _ => HealthState.Ok,
                },
            Primary = tightest is null
                ? "—"
                : $"{tightest.DriveLetter} {tightest.FreeBytes / 1_000_000_000.0:0} GB",
            Lines = volumes is null || volumes.Count == 0
                ? new[] { "sin datos" }
                : volumes.Take(3)
                    .Select(v => $"{v.DriveLetter} {v.FreePercent:0}% libre")
                    .ToArray(),
        };
    }

    [RelayCommand]
    private async Task RunPreShowAsync()
    {
        IsBusy = true;
        try
        {
            ApiResult<PreShowVerdict> result =
                await Nodes.Api().RunPreShowAsync().ConfigureAwait(true);

            PreShowResult = result.Success ? result.Value : null;
            Error = result.Success ? null : result.Error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    private PreShowVerdict? _preShowResult;

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{uptime.Days} d {uptime.Hours} h"
        : $"{uptime.Hours} h {uptime.Minutes} min";

    private static string Shorten(string value, int max = 22) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>
/// Una tarjeta del dashboard: un indicador de estado y dos o tres cifras. Nada mas.
/// Si necesitas leer un parrafo para saber si algo va bien, la tarjeta esta mal disenada.
/// </summary>
public sealed class StatusCardViewModel
{
    private IRelayCommand? _activateCommand;

    public required string Title { get; init; }
    public required HealthState Health { get; init; }
    public required string Primary { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    /// <summary>Seccion a la que navega al hacer clic.</summary>
    public string? Section { get; init; }

    public Action<string>? Navigate { get; init; }

    /// <summary>Cada tarjeta navega a su sección. Todo dato es clicable hacia su origen.</summary>
    public IRelayCommand ActivateCommand => _activateCommand ??= new RelayCommand(Activate);

    private void Activate()
    {
        if (Section is not null)
        {
            Navigate?.Invoke(Section);
        }
    }
}

/// <summary>Una serie de ping para el grafico del dashboard.</summary>
public sealed class PingSeriesViewModel
{
    public PingSeriesViewModel(PingStatistics stats, IReadOnlyList<PingSample>? window = null)
    {
        Target = stats.Target;
        Samples = window is null
            ? Array.Empty<double?>()
            : window.Select(s => s.RttMs).ToList();

        // Techo del eje adaptado a lo que realmente pasa en este enlace, con un minimo
        // de 20 ms para que una LAN sana no se pinte como una montana rusa.
        double observed = Samples.Where(v => v is not null).DefaultIfEmpty(0).Max() ?? 0;
        CeilingMs = Math.Max(20, Math.Ceiling(observed * 1.25));

        Average = stats.AverageMs;
        Jitter = stats.JitterMs;
        LossPercent = stats.LossPercent;
        Health = PingStatisticsCalculator.Evaluate(stats);
    }

    public string Target { get; }
    public IReadOnlyList<double?> Samples { get; }
    public double CeilingMs { get; }
    public double? Average { get; }
    public double? Jitter { get; }
    public double LossPercent { get; }
    public HealthState Health { get; }

    public string Summary => Average is double avg
        ? $"{avg:0.#} ms · jitter {Jitter:0.#} ms · pérdida {LossPercent:0.#} %"
        : $"sin respuesta · pérdida {LossPercent:0.#} %";
}
