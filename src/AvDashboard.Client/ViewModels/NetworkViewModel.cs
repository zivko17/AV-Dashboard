using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Red (M5). Interfaces, ping continuo y escaneo del segmento.
/// El grafico de ping tambien esta en el dashboard a proposito: es lo que mas veces
/// da la pista primero, y esconderlo aqui seria un error.
/// </summary>
public sealed partial class NetworkViewModel : PageViewModel
{
    public NetworkViewModel(NodeManager nodes, IUiShell shell) : base(nodes, shell)
    {
    }

    public override string Title => "Red";
    public override string Glyph => "\uE968";

    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<PingSeriesViewModel> Pings { get; } = new();
    public ObservableCollection<DiscoveredDevice> Discovered { get; } = new();

    [ObservableProperty]
    private string? _scanRange;

    [ObservableProperty]
    private string _scanSummary = "Sin escanear.";

    public override async Task ActivateAsync()
    {
        await base.ActivateAsync().ConfigureAwait(true);

        TelemetryClient telemetry = Nodes.Telemetry();
        await telemetry.StartAsync().ConfigureAwait(true);

        // Solo throughput: el detalle de ping de esta vista es la estadistica agregada,
        // que se pide bajo demanda. El grafico en vivo esta en el dashboard.
        await telemetry.SubscribeAsync(TelemetryChannels.NetworkThroughput).ConfigureAwait(true);
    }

    public override async Task DeactivateAsync()
    {
        await Nodes.Telemetry().UnsubscribeAsync(TelemetryChannels.NetworkThroughput).ConfigureAwait(true);

        await base.DeactivateAsync().ConfigureAwait(true);
    }

    protected override async Task LoadAsync()
    {
        ApiClient api = Nodes.Api();

        ApiResult<NetworkResponse> network = await api.GetNetworkAsync().ConfigureAwait(true);
        if (!Apply(network, out NetworkResponse value))
        {
            return;
        }

        Health = value.Health;

        Interfaces.Clear();
        foreach (NetworkInterfaceInfo nic in value.Interfaces.OrderByDescending(i => i.IsUp)
                     .ThenBy(i => i.Name, StringComparer.CurrentCulture))
        {
            Interfaces.Add(nic);
        }

        ApiResult<IReadOnlyList<PingStatistics>> pings = await api.GetPingAsync().ConfigureAwait(true);

        Pings.Clear();
        foreach (PingStatistics stats in pings.Value ?? Array.Empty<PingStatistics>())
        {
            Pings.Add(new PingSeriesViewModel(stats));
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        ScanSummary = "Escaneando…";

        try
        {
            ScanRequest request = new()
            {
                Cidr = string.IsNullOrWhiteSpace(ScanRange) ? null : ScanRange.Trim(),
            };

            ApiResult<ScanResponse> result = await Nodes.Api().ScanAsync(request).ConfigureAwait(true);

            if (!result.Success)
            {
                ScanSummary = result.Error ?? "El escaneo ha fallado.";
                return;
            }

            ScanResponse scan = result.Value!;

            Discovered.Clear();
            foreach (DiscoveredDevice device in scan.Devices)
            {
                Discovered.Add(device);
            }

            ScanRange ??= scan.Range;
            ScanSummary = $"{scan.Devices.Count} dispositivos en {scan.Range} " +
                          $"({scan.Duration.TotalSeconds:0.#} s).";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
