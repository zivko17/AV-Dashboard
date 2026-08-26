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

    public ObservableCollection<AlertRow> ActiveAlerts { get; } = new();
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
        Cards.Add(BuildVideoCard(displays.Value, system.Value));
        Cards.Add(BuildNetworkCard(network.Value, pings.Value));
        Cards.Add(BuildStorageCard(storage.Value));
        Cards.Add(BuildAudioCard(audio.Value));
        Cards.Add(BuildServicesCard(services.Value));

        ActiveAlerts.Clear();
        foreach (Alert alert in alerts.Value ?? Array.Empty<Alert>())
        {
            ActiveAlerts.Add(new AlertRow(alert));
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

    /// <summary>Como mucho tres medidores por tarjeta, o la rejilla se rompe.</summary>
    internal const int MaxGauges = 3;

    /// <summary>
    /// Los medidores de una seccion, recortados a los que caben.
    ///
    /// <paramref name="bySeverity"/> distingue los dos casos, y la distincion importa. Una
    /// lista de cosas iguales —tres tarjetas de red, cuatro discos— no tiene orden natural,
    /// y ahi si hay que ensenar la peor: con tres NIC, la que hay que ver es la saturada y
    /// no la primera que enumere Windows.
    ///
    /// Pero CPU y RAM, o GPU y VRAM, no son intercambiables: cada tarjeta tiene su titular.
    /// Ordenarlas por porcentaje hacia que la tarjeta se rebarajara sola en cada refresco
    /// —hoy CPU arriba, dentro de un minuto RAM— y un panel que cambia de sitio sus filas no
    /// se puede leer de memoria, que es justo como se lee cuando hay prisa.
    /// </summary>
    internal static (IReadOnlyList<LoadGaugeViewModel> Shown, string? More) Rank(
        IEnumerable<LoadGaugeViewModel> gauges, bool bySeverity = false)
    {
        List<LoadGaugeViewModel> all = bySeverity
            ? gauges.OrderByDescending(g => g.Percent ?? -1).ToList()
            : gauges.ToList();

        if (all.Count <= MaxGauges)
        {
            return (all, null);
        }

        int hidden = all.Count - MaxGauges;

        return (all.Take(MaxGauges).ToList(),
            hidden == 1 ? "y 1 más" : $"y {hidden} más");
    }

    private static IEnumerable<LoadGaugeViewModel> SystemGauges(SystemStatus? status)
    {
        yield return new LoadGaugeViewModel(
            "CPU", LoadMetric.Cpu, status?.CpuTotalPercent,
            detail: status is null
                ? null
                : status.CpuTemperatureC is double temp
                    ? $"{temp:0} °C · {status.CpuPerCorePercent.Count} hilos"
                    : $"{status.CpuPerCorePercent.Count} hilos",
            unavailableReason: "el agente aún no ha leído nada");

        yield return new LoadGaugeViewModel(
            "RAM", LoadMetric.Memory, status?.MemoryUsedPercent,
            detail: status is null
                ? null
                : $"{Gigabytes(status.MemoryUsedBytes)} / {Gigabytes(status.MemoryTotalBytes)} GB",
            unavailableReason: "el agente aún no ha leído nada");
    }

    private static IEnumerable<LoadGaugeViewModel> VideoGauges(SystemStatus? status)
    {
        yield return new LoadGaugeViewModel(
            "GPU", LoadMetric.Gpu, status?.Gpu?.UsagePercent,
            detail: status?.GpuTemperatureC is double temp ? $"{temp:0} °C" : null,

            // Los contadores de rendimiento se corrompen a menudo, y esta es la unica
            // lectura del panel que depende de ellos. Decirlo es mejor que un cero verde.
            unavailableReason: status is null
                ? "el agente aún no ha leído nada"
                : "sin contadores de GPU");

        // La VRAM solo sale si se conoce el total: sin el no hay porcentaje, y el dato en
        // crudo no dice nada por su cuenta.
        if (status?.Gpu?.MemoryUsedPercent is double vram)
        {
            yield return new LoadGaugeViewModel(
                "VRAM", LoadMetric.VideoMemory, vram,
                detail: $"{Gigabytes(status.Gpu.MemoryUsedBytes ?? 0)} / " +
                        $"{Gigabytes(status.Gpu.MemoryTotalBytes ?? 0)} GB");
        }
    }

    private static IEnumerable<LoadGaugeViewModel> NetworkGauges(NetworkResponse? network) =>
        (network?.Interfaces ?? Array.Empty<NetworkInterfaceInfo>())
            // Solo tarjetas fisicas: un adaptador virtual declara una velocidad inventada
            // —el de Tailscale dice 95 Gb/s— asi que su ocupacion sale siempre 0 % y solo
            // ocupa sitio al lado de las que si dicen algo.
            .Where(n => n.IsUp && n.IsPhysical && n.LinkSpeedBps > 0)
            .Select(nic =>
            {
                double megabits = nic.ReceiveMbps + nic.SendMbps;
                double capacity = nic.LinkSpeedBps / 1_000_000.0;

                return new LoadGaugeViewModel(
                    nic.Name, LoadMetric.NetworkLink,
                    Math.Min(megabits * 100 / capacity, 100),
                    detail: $"{megabits:0.0} de {capacity:0} Mb/s");
            });

    private static IEnumerable<LoadGaugeViewModel> StorageGauges(IReadOnlyList<StorageVolume>? volumes) =>
        (volumes ?? Array.Empty<StorageVolume>()).Select(volume => new LoadGaugeViewModel(
            string.IsNullOrWhiteSpace(volume.Label)
                ? volume.DriveLetter
                : $"{volume.DriveLetter} {volume.Label}",

            // Ocupado, no libre. «Al 8 %» queriendo decir 8 % libre es la clase de
            // ambiguedad que se lee al reves justo cuando hay prisa.
            LoadMetric.DiskSpace, 100 - volume.FreePercent,
            detail: $"{Gigabytes(volume.TotalBytes - volume.FreeBytes)} / {Gigabytes(volume.TotalBytes)} GB"));

    private static string Gigabytes(long bytes) => (bytes / 1024.0 / 1024 / 1024).ToString("0.#");

    private StatusCardViewModel BuildSystemCard(SystemResponse? system) => new()
    {
        Title = "SISTEMA",
        Section = "Sistema",
        Navigate = _navigate,
        Health = system is null
            ? HealthState.Unknown
            : system.Status.CpuTotalPercent > 95 ? HealthState.Warning : HealthState.Ok,
        // La CPU y la RAM ya no van aqui como texto: son los medidores de la tarjeta. Lo
        // que queda son los datos que no se miden en porcentaje.
        Gauges = Rank(SystemGauges(system?.Status)).Shown,
        Primary = string.Empty,
        Lines = system is null
            ? new[] { "sin datos" }
            : new[]
            {
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
                Plural(audio.Devices.Count(d => d.State == AudioDeviceState.Active),
                    "endpoint activo", "endpoints activos"),
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

        (IReadOnlyList<LoadGaugeViewModel> gauges, string? more) = Rank(NetworkGauges(network), bySeverity: true);

        return new StatusCardViewModel
        {
            Title = "RED",
            Section = "Red",
            Navigate = _navigate,
            Health = network?.Health ?? HealthState.Unknown,
            Gauges = gauges,
            MoreText = more,
            Primary = gauges.Count > 0
                ? string.Empty
                : primary is null ? "sin enlace" : $"{primary.Name} {primary.Ipv4Addresses[0]}",
            Lines = network is null
                ? new[] { "sin datos" }
                : new[]
                {
                    primary is null ? "sin enlace" : $"{primary.Name} {primary.Ipv4Addresses[0]}",
                    worstRtt is double rtt ? $"peor RTT {rtt:0.#} ms" : "sin ping continuo",
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
    private StatusCardViewModel BuildVideoCard(DisplayTopology? topology, SystemResponse? system)
    {
        (IReadOnlyList<LoadGaugeViewModel> gauges, string? more) = Rank(VideoGauges(system?.Status));

        if (topology is null || topology.Outputs.Count == 0)
        {
            return new StatusCardViewModel
            {
                Title = "VÍDEO",
                Health = HealthState.Unknown,
                Primary = "sin salidas leídas",
                Gauges = gauges,
                MoreText = more,
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
            Primary = string.Empty,
            Gauges = gauges,
            MoreText = more,

            // La cuenta de salidas baja a las lineas: el titular de esta tarjeta ahora son
            // los medidores de GPU y VRAM.
            Lines = lines.Prepend(topology.ActiveCount == 1
                ? "1 salida activa"
                : $"{topology.ActiveCount} salidas activas").Take(3).ToList(),
            Section = "Vídeo",
            Navigate = _navigate,
        };
    }

    private StatusCardViewModel BuildStorageCard(IReadOnlyList<StorageVolume>? volumes)
    {
        StorageVolume? tightest = volumes?.OrderBy(v => v.FreePercent).FirstOrDefault();

        (IReadOnlyList<LoadGaugeViewModel> gauges, string? more) = Rank(StorageGauges(volumes), bySeverity: true);

        return new StatusCardViewModel
        {
            Gauges = gauges,
            MoreText = more,
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
            Primary = gauges.Count > 0 ? string.Empty
                : tightest is null ? "sin unidades leídas"
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

    /// <summary>
    /// «1 endpoints activos» delata que nadie ha mirado la pantalla con un solo dispositivo,
    /// y en un rig con una sola salida se ve el primer dia.
    /// </summary>
    private static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static string Shorten(string value, int max = 22) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>
/// Una tarjeta del dashboard: una seccion, lo que se puede medir de ella y dos o tres
/// cifras de contexto. Si necesitas leer un parrafo para saber si algo va bien, la tarjeta
/// esta mal disenada.
///
/// Antes esto convivia con una fila de medidores aparte, y la CPU salia dos veces: en su
/// medidor y otra vez dentro de la tarjeta de Sistema. Lo mismo la RAM y el disco. Ahora la
/// seccion es la unidad y las medidas viven dentro de ella, que es lo que evita tener que
/// mirar dos sitios para lo mismo.
/// </summary>
public sealed class StatusCardViewModel
{
    private IRelayCommand? _activateCommand;

    public required string Title { get; init; }
    public required HealthState Health { get; init; }

    /// <summary>
    /// El titular de las secciones que no se miden en porcentaje.
    ///
    /// Audio y Servicios no tienen uno y no se les inventa: un numero falso es peor que
    /// ninguno. Esas tarjetas ensenan su veredicto aqui y las demas ensenan sus medidores.
    /// </summary>
    public required string Primary { get; init; }

    /// <summary>
    /// Lo medible de esta seccion, lo peor primero.
    ///
    /// El orden no es el de la API: si tienes tres tarjetas de red y la tercera esta
    /// saturada, es esa la que tiene que verse, no la primera por orden de enumeracion.
    /// </summary>
    public IReadOnlyList<LoadGaugeViewModel> Gauges { get; init; } = Array.Empty<LoadGaugeViewModel>();

    public bool HasGauges => Gauges.Count > 0;

    /// <summary>Cuando hay mas medidas de las que caben, se dice cuantas quedan fuera.</summary>
    public string? MoreText { get; init; }

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

/// <summary>
/// Una alerta activa en la lista del dashboard.
///
/// Solo la hora necesita formato, pero en WinUI los enlaces no tienen <c>StringFormat</c> y
/// escribirla en el XAML significaria escribirla dos veces, una por interfaz.
/// </summary>
public sealed class AlertRow
{
    public AlertRow(Alert source)
    {
        Alert = source;
    }

    public Alert Alert { get; }

    public Severity Severity => Alert.Severity;
    public string Module => Alert.Module;
    public string Message => Alert.Message;

    /// <summary>Sin fecha: una alerta activa es de hoy, y la hora con segundos es lo que se correlaciona.</summary>
    public string TimeText => Alert.RaisedAt.LocalDateTime.ToString("HH:mm:ss");
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
