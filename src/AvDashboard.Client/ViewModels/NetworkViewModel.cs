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

    public ObservableCollection<NetworkInterfaceRow> Interfaces { get; } = new();
    public ObservableCollection<PingSeriesViewModel> Pings { get; } = new();

    /// <summary>
    /// El inventario persistido, no el resultado del ultimo barrido.
    ///
    /// La diferencia importa: lo que escaneaste hace media hora sigue aqui, con su alias y
    /// su estrella, y un equipo que hoy no ha contestado al barrido no desaparece de la
    /// lista solo porque el multicast del venue este filtrado.
    /// </summary>
    public ObservableCollection<DeviceRow> Inventory { get; } = new();

    [ObservableProperty]
    private string? _scanRange;

    [ObservableProperty]
    private string _scanSummary = "Sin escanear.";

    /// <summary>Direccion del alta manual. Solo IP: ver el motivo en el endpoint.</summary>
    [ObservableProperty]
    private string? _newAddress;

    [ObservableProperty]
    private string? _newAlias;

    [ObservableProperty]
    private string? _inventoryError;

    private DateTimeOffset? _lastScanAt;

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
            Interfaces.Add(new NetworkInterfaceRow(nic));
        }

        ApiResult<IReadOnlyList<PingStatistics>> pings = await api.GetPingAsync().ConfigureAwait(true);

        Pings.Clear();
        foreach (PingStatistics stats in pings.Value ?? Array.Empty<PingStatistics>())
        {
            Pings.Add(new PingSeriesViewModel(stats));
        }

        await LoadInventoryAsync(pings.Value).ConfigureAwait(true);
    }

    /// <summary>
    /// Rehace la tabla del inventario. Las estadisticas de ping entran aqui porque son las
    /// que deciden si un equipo vigilado esta midiendo, responde o se ha caido.
    /// </summary>
    private async Task LoadInventoryAsync(IReadOnlyList<PingStatistics>? pings)
    {
        ApiResult<InventoryResponse> result =
            await Nodes.Api().GetDevicesAsync().ConfigureAwait(true);

        if (!result.Success || result.Value is null)
        {
            InventoryError = result.Error;
            return;
        }

        InventoryError = null;
        _lastScanAt = result.Value.LastScanAt;

        // Las estadisticas van por direccion, que es como las publica el colector.
        Dictionary<string, PingStatistics> byAddress = new(StringComparer.OrdinalIgnoreCase);

        foreach (PingStatistics stats in pings ?? Array.Empty<PingStatistics>())
        {
            byAddress[stats.Target] = stats;
        }

        DateTimeOffset now = DateTimeOffset.Now;

        Inventory.Clear();
        foreach (DiscoveredDevice device in result.Value.Devices)
        {
            // El colector etiqueta la serie con el alias cuando lo hay, asi que se busca por
            // los dos: si no, un equipo con nombre no encontraria nunca sus estadisticas y
            // saldria «midiendo» para siempre.
            PingStatistics? stats = null;

            if (!byAddress.TryGetValue(device.IpAddress, out stats) && device.Alias is string alias)
            {
                byAddress.TryGetValue(alias, out stats);
            }

            Inventory.Add(new DeviceRow(
                device, stats, _lastScanAt, now, SaveDeviceAsync, RemoveDeviceAsync));
        }
    }

    /// <summary>
    /// Da de alta un equipo escrito a mano.
    ///
    /// Entra vigilado: si lo escribes tu es porque te importa. Lo que no se hace es marcar
    /// como criticos los del barrido, que dejaria el ping corriendo contra todo lo que haya
    /// en la red del venue.
    /// </summary>
    [RelayCommand]
    private async Task AddDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAddress))
        {
            return;
        }

        ApiResult<DiscoveredDevice> result = await Nodes.Api().SaveDeviceAsync(new DeviceUpsertRequest
        {
            IpAddress = NewAddress.Trim(),
            Alias = string.IsNullOrWhiteSpace(NewAlias) ? null : NewAlias.Trim(),
            IsCritical = true,
        }).ConfigureAwait(true);

        if (!result.Success)
        {
            InventoryError = result.Error;
            return;
        }

        NewAddress = null;
        NewAlias = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task SaveDeviceAsync(DeviceRow row)
    {
        ApiResult<DiscoveredDevice> result = await Nodes.Api().SaveDeviceAsync(new DeviceUpsertRequest
        {
            IpAddress = row.IpAddress,

            // Cadena vacia borra el alias; null lo dejaria como estaba, y entonces no
            // habria forma de quitarle el nombre a un equipo.
            Alias = row.Alias ?? string.Empty,
            IsCritical = row.IsCritical,
        }).ConfigureAwait(true);

        InventoryError = result.Success ? null : result.Error;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RemoveDeviceAsync(DeviceRow row)
    {
        ApiResult<bool> result = await Nodes.Api()
            .DeleteDeviceAsync(row.IpAddress).ConfigureAwait(true);

        InventoryError = result.Success ? null : result.Error;
        await RefreshAsync().ConfigureAwait(true);
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

            ScanRange ??= scan.Range;
            ScanSummary = $"{scan.Devices.Count} dispositivos en {scan.Range} " +
                          $"({scan.Duration.TotalSeconds:0.#} s).";

            // El barrido ya no pinta su propia lista: vuelca al inventario en el agente y
            // aqui se recarga. Ensenar el resultado del escaneo aparte del inventario
            // obligaba a mirar dos tablas para saber si algo estaba marcado.
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Una interfaz de red en la tabla.
///
/// Como la fila del Timeline, existe para dar formato: en WinUI los enlaces no tienen
/// <c>StringFormat</c>, y la velocidad de enlace y el caudal son justo dos números que sin
/// unidad no dicen nada. El aviso de power management se escribe entero aquí por lo mismo.
/// </summary>
public sealed class NetworkInterfaceRow
{
    public NetworkInterfaceRow(NetworkInterfaceInfo source)
    {
        Interface = source;
    }

    public NetworkInterfaceInfo Interface { get; }

    public string Name => Interface.Name;
    public string? Description => Interface.Description;
    public string? SubnetMask => Interface.SubnetMask;

    public string Ipv4Text => Interface.Ipv4Addresses.Count > 0
        ? Interface.Ipv4Addresses[0]
        : "sin IPv4";

    /// <summary>
    /// Un gigabit que ha negociado a 100 Mb se lee aquí, y es la avería silenciosa que más
    /// veces explica un NDI que va a tirones. Por eso sale en Mb/s y no en bits crudos.
    /// </summary>
    public string LinkSpeedText => Interface.LinkSpeedBps <= 0
        ? "sin enlace"
        : Interface.LinkSpeedBps >= 1_000_000_000
            ? $"{Interface.LinkSpeedBps / 1_000_000_000.0:0.#} Gb/s"
            : $"{Interface.LinkSpeedBps / 1_000_000.0:0.#} Mb/s";

    public string ThroughputText => $"↓ {Interface.ReceiveMbps:0.0} Mb/s";

    public bool PowerManagementWarning => Interface.PowerManagementEnabled == true;

    public string PowerManagementText =>
        "⚠ power management activo — puede tumbar Dante y NDI en silencio";
}
