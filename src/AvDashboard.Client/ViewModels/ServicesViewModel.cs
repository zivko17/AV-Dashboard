using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Servicios (M6), agrupados por sector AV y no por el orden alfabetico de services.msc.
/// El contador por sector cuenta servicios esperados en marcha: uno parado que debe
/// estarlo no genera aviso.
/// </summary>
public sealed partial class ServicesViewModel : PageViewModel
{
    public ServicesViewModel(NodeManager nodes, IUiShell shell) : base(nodes, shell)
    {
    }

    public override string Title => "Servicios";
    public override string Glyph => "\uE945";

    public ObservableCollection<ServiceSectorRow> Sectors { get; } = new();

    /// <summary>
    /// Lo ultimo que ha contestado el agente, sin filtrar.
    ///
    /// El filtro y la casilla de «solo AV» se aplican sobre esto y no volviendo a pedir la
    /// lista: son trescientos servicios que el agente saca de WMI, y pedirlos otra vez por
    /// cada tecla pulsada hacia dos cosas mal a la vez. La primera, trabajo para nada. La
    /// segunda, peor: RefreshAsync se sale si ya hay una llamada en curso, asi que al
    /// escribir rapido se perdian pulsaciones y la lista acababa sin corresponder con lo
    /// escrito, que es la clase de fallo que te hace dudar de lo que ves en pantalla.
    /// </summary>
    private ServicesResponse? _lastResponse;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _onlyAv = true;

    [ObservableProperty]
    private string _baselineSummary = "Sin baseline tomado.";

    protected override async Task LoadAsync()
    {
        ApiResult<ServicesResponse> result = await Nodes.Api().GetServicesAsync().ConfigureAwait(true);
        if (!Apply(result, out ServicesResponse services))
        {
            return;
        }

        _lastResponse = services;
        Health = services.Health;

        Project();

        BaselineSummary = services.BaselineTakenAt is DateTimeOffset taken
            ? $"Baseline del {taken.LocalDateTime:dd/MM/yyyy HH:mm}."
            : "Sin baseline tomado. Tómalo cuando la máquina esté como debe estar.";
    }

    /// <summary>Vuelca la ultima respuesta en la coleccion, aplicando filtro y «solo AV».</summary>
    private void Project()
    {
        Sectors.Clear();

        if (_lastResponse is null)
        {
            return;
        }

        foreach (ServiceSectorGroup group in _lastResponse.Sectors)
        {
            if (OnlyAv && group.Sector is Sector.Unclassified or Sector.SystemEssential)
            {
                continue;
            }

            Sectors.Add(new ServiceSectorRow(Filtered(group)));
        }
    }

    private ServiceSectorGroup Filtered(ServiceSectorGroup group)
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return group;
        }

        string needle = Filter.Trim();

        return group with
        {
            Services = group.Services
                .Where(s => s.DisplayName.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                            s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList(),
        };
    }

    partial void OnFilterChanged(string value) => Project();

    partial void OnOnlyAvChanged(bool value) => Project();

    [RelayCommand]
    private async Task TakeBaselineAsync()
    {
        // Tomar baseline no toca la maquina: solo fija el punto de comparacion contra el
        // que se responde «¿qué ha cambiado desde la última vez que esto funcionaba?».
        ApiResult<ServiceBaselineResult> result =
            await Nodes.Api().TakeServiceBaselineAsync().ConfigureAwait(true);

        if (!result.Success)
        {
            Error = result.Error;
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }
}

/// <summary>Un sector con su contador ya escrito.</summary>
public sealed class ServiceSectorRow
{
    public ServiceSectorRow(ServiceSectorGroup group)
    {
        Group = group;
        Services = group.Services.Select(s => new ServiceRow(s)).ToList();
    }

    public ServiceSectorGroup Group { get; }

    public string DisplayName => Group.DisplayName;
    public HealthState Health => Group.Health;

    public IReadOnlyList<ServiceRow> Services { get; }

    /// <summary>«3/5 esperados en marcha». Un sector sin esperados lo dice, no pone 0/0.</summary>
    public string ExpectedText => Group.TotalExpected == 0
        ? "sin servicios esperados"
        : $"{Group.RunningExpected}/{Group.TotalExpected} esperados en marcha";
}

/// <summary>
/// Un servicio en la tabla.
///
/// El estado y el modo de arranque salen traducidos: son enumeraciones de Windows con los
/// nombres en inglés, y dejarlas crudas ponía «Running» y «AutomaticDelayed» en una interfaz
/// que está entera en castellano.
/// </summary>
public sealed class ServiceRow
{
    public ServiceRow(ServiceInfo service)
    {
        Service = service;
    }

    public ServiceInfo Service { get; }

    public string Name => Service.Name;
    public string DisplayName => Service.DisplayName;
    public int? ProcessId => Service.ProcessId;
    public bool IsUnexpectedlyStopped => Service.IsUnexpectedlyStopped;

    public string StateText => Service.State switch
    {
        ServiceState.Running => "en marcha",
        ServiceState.Stopped => "parado",
        ServiceState.Paused => "en pausa",
        ServiceState.StartPending => "arrancando",
        ServiceState.StopPending => "parándose",
        ServiceState.ContinuePending => "reanudándose",
        ServiceState.PausePending => "pausándose",
        _ => "desconocido",
    };

    public string StartModeText => Service.StartMode switch
    {
        ServiceStartMode.Automatic => "automático",
        ServiceStartMode.AutomaticDelayed => "automático (retardado)",
        ServiceStartMode.Manual => "manual",
        ServiceStartMode.Disabled => "deshabilitado",
        ServiceStartMode.Boot => "arranque",
        ServiceStartMode.System => "sistema",
        _ => "desconocido",
    };

    public string StoppedWarning => "⚠ se esperaba en marcha";
}
