using System.Collections.ObjectModel;
using AvDashboard.Client;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Servicios (M6), agrupados por sector AV y no por el orden alfabetico de services.msc.
/// El contador por sector cuenta servicios esperados en marcha: uno parado que debe
/// estarlo no genera aviso.
/// </summary>
public sealed partial class ServicesViewModel : PageViewModel
{
    public ServicesViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Servicios";
    public override string Glyph => "\uE945";

    public ObservableCollection<ServiceSectorGroup> Sectors { get; } = new();

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

        Health = services.Health;

        Sectors.Clear();
        foreach (ServiceSectorGroup group in services.Sectors)
        {
            if (OnlyAv && group.Sector is Sector.Unclassified or Sector.SystemEssential)
            {
                continue;
            }

            Sectors.Add(Filtered(group));
        }

        BaselineSummary = services.BaselineTakenAt is DateTimeOffset taken
            ? $"Baseline del {taken.LocalDateTime:dd/MM/yyyy HH:mm}."
            : "Sin baseline tomado. Tómalo cuando la máquina esté como debe estar.";
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

    partial void OnFilterChanged(string value) => _ = RefreshAsync();

    partial void OnOnlyAvChanged(bool value) => _ = RefreshAsync();

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
