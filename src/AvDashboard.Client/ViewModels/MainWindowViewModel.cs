using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// La ventana: rail lateral, barra superior y area de contenido.
///
/// La barra superior es persistente en todas las vistas y lleva el selector de nodo, el
/// semaforo global, el perfil aplicado y el boton de panico. Todo lo que se pinta debajo
/// corresponde al nodo seleccionado, sin ambiguedad posible.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly NodeManager _nodes;
    private readonly PeriodicTimer _heartbeat = new(TimeSpan.FromSeconds(5));

    public MainWindowViewModel(NodeManager nodes, IUiShell shell)
    {
        _nodes = nodes;

        Pages = new ObservableCollection<PageViewModel>
        {
            new DashboardViewModel(nodes, shell, NavigateTo),
            new SystemViewModel(nodes, shell),
            new AudioViewModel(nodes, shell),
            new VideoViewModel(nodes, shell),
            new NetworkViewModel(nodes, shell),
            new ServicesViewModel(nodes, shell),
            new ProfilesViewModel(nodes, shell),
            new PlaceholderViewModel(nodes, shell, "Nodos", "\uE977", "Fase 3",
                "Vista consolidada de la flota y lanzador de RDP, VNC y web para máquinas sin agente."),
            new TimelineViewModel(nodes, shell),
        };

        Current = Pages[0];
        NodeName = nodes.Selected.Name;

        if (nodes.Selected.Token is null)
        {
            ConnectionStatus = AgentEndpoint.DescribeMissingToken();
        }
    }

    public ObservableCollection<PageViewModel> Pages { get; }

    public CommandPaletteViewModel Palette { get; } = new();

    [ObservableProperty]
    private PageViewModel _current;

    [ObservableProperty]
    private string _nodeName;

    [ObservableProperty]
    private HealthState _nodeHealth = HealthState.Unknown;

    [ObservableProperty]
    private string _activeProfile = "sin perfil";

    [ObservableProperty]
    private int _activeAlertCount;

    [ObservableProperty]
    private string _connectionStatus = "Conectando con el agente…";

    [ObservableProperty]
    private bool _isPaletteOpen;

    public async Task InitializeAsync()
    {
        await Current.ActivateAsync().ConfigureAwait(true);
        _ = HeartbeatLoopAsync();
    }

    /// <summary>
    /// Sondeo ligero de cabecera cada 5 s. Es la unica llamada que la consola hace sin
    /// que el usuario lo pida, y devuelve tres campos.
    /// </summary>
    private async Task HeartbeatLoopAsync()
    {
        while (await _heartbeat.WaitForNextTickAsync().ConfigureAwait(true))
        {
            ApiResult<NodeResponse> result = await _nodes.Api().GetNodeAsync().ConfigureAwait(true);

            if (result.Success)
            {
                NodeResponse node = result.Value!;
                NodeHealth = node.Health;
                ActiveAlertCount = node.ActiveAlerts;
                ActiveProfile = node.Node.ActiveProfile ?? "sin perfil";
                ConnectionStatus = $"Agente {node.Node.AgentVersion} · {node.Node.Hostname}";
                continue;
            }

            NodeHealth = HealthState.Unknown;
            ConnectionStatus = result.Error ?? "El agente no responde.";

            // Un 401 despues de haber funcionado casi siempre significa que el servicio
            // se ha reiniciado y ha rotado el token. Se reintenta leyendolo otra vez.
            if (result.Error?.Contains("token", StringComparison.OrdinalIgnoreCase) == true)
            {
                _nodes.RefreshLocalToken();
            }
        }
    }

    /// <summary>
    /// El rail enlaza su SelectedItem con <see cref="Current"/>, asi que el cambio de
    /// pagina pasa siempre por aqui: venga del rail, de una tarjeta del dashboard o de
    /// la paleta de comandos. Un unico sitio donde se activa y se desactiva.
    /// </summary>
    partial void OnCurrentChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        _ = SwitchAsync(oldValue, newValue);
    }

    private static async Task SwitchAsync(PageViewModel? previous, PageViewModel next)
    {
        if (previous is not null)
        {
            await previous.DeactivateAsync().ConfigureAwait(true);
        }

        await next.ActivateAsync().ConfigureAwait(true);
    }

    /// <summary>Navegacion por nombre, que es lo que usan las tarjetas del dashboard.</summary>
    private void NavigateTo(string title)
    {
        PageViewModel? page = Pages.FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));

        if (page is not null)
        {
            Current = page;
        }
    }

    [RelayCommand]
    private void TogglePalette()
    {
        IsPaletteOpen = !IsPaletteOpen;
        if (IsPaletteOpen)
        {
            Palette.Reset();
        }
    }

    [RelayCommand]
    private Task RefreshCurrentAsync() => Current.RefreshAsync();
}
