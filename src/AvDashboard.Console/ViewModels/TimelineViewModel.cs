using System.Collections.ObjectModel;
using AvDashboard.Client;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Show Timeline (M11): todos los modulos en una sola linea temporal.
/// Es lo que convierte «se cayó algo a las nueve» en «pérdida de display a las 21:04:12,
/// pico de ping a las 21:04:11, servicio caído a las 21:04:09».
/// </summary>
public sealed partial class TimelineViewModel : PageViewModel
{
    public TimelineViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Timeline";
    public override string Glyph => "\uE81C";

    public ObservableCollection<TimelineEvent> Events { get; } = new();

    /// <summary>Ventana en minutos. Dos horas por defecto: la duracion de un show.</summary>
    [ObservableProperty]
    private int _windowMinutes = 120;

    [ObservableProperty]
    private Severity _minimumSeverity = Severity.Info;

    [ObservableProperty]
    private string _summary = string.Empty;

    protected override async Task LoadAsync()
    {
        DateTimeOffset to = DateTimeOffset.UtcNow;
        DateTimeOffset from = to.AddMinutes(-WindowMinutes);

        ApiResult<TimelineResponse> result = await Nodes.Api()
            .GetTimelineAsync(from, to).ConfigureAwait(true);

        if (!Apply(result, out TimelineResponse timeline))
        {
            return;
        }

        Events.Clear();
        foreach (TimelineEvent item in timeline.Events.Where(e => e.Severity >= MinimumSeverity))
        {
            Events.Add(item);
        }

        Summary = timeline.Truncated
            ? $"{Events.Count} eventos (truncado; acota la ventana)."
            : $"{Events.Count} eventos en los últimos {WindowMinutes} minutos.";

        Health = Events.Any(e => e.Severity >= Severity.Critical)
            ? HealthState.Critical
            : Events.Any(e => e.Severity >= Severity.Warning)
                ? HealthState.Warning
                : HealthState.Ok;
    }

    partial void OnWindowMinutesChanged(int value) => _ = RefreshAsync();

    partial void OnMinimumSeverityChanged(Severity value) => _ = RefreshAsync();
}
