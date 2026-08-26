using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Show Timeline (M11): todos los modulos en una sola linea temporal.
/// Es lo que convierte «se cayó algo a las nueve» en «pérdida de display a las 21:04:12,
/// pico de ping a las 21:04:11, servicio caído a las 21:04:09».
/// </summary>
public sealed partial class TimelineViewModel : PageViewModel
{
    public TimelineViewModel(NodeManager nodes, IUiShell shell) : base(nodes, shell)
    {
    }

    public override string Title => "Timeline";
    public override string Glyph => "\uE81C";

    public ObservableCollection<TimelineEventRow> Events { get; } = new();

    /// <summary>Ventana en minutos. Dos horas por defecto: la duracion de un show.</summary>
    [ObservableProperty]
    private int _windowMinutes = 120;

    /// <summary>
    /// Ventanas ofrecidas en el desplegable.
    ///
    /// Son las cuatro que se piden de verdad: lo que acaba de pasar, la ultima hora, el
    /// show entero y la jornada de montaje. Escribir un numero a mano delante del cliente
    /// es una operacion mas de las que hacen falta.
    /// </summary>
    public IReadOnlyList<int> WindowOptions { get; } = new[] { 15, 60, 120, 480 };

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
            Events.Add(new TimelineEventRow(item));
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

/// <summary>
/// Una fila del Timeline.
///
/// Existe solo para dar formato. WinUI no tiene <c>StringFormat</c> en los enlaces, así que
/// la hora y el nombre del sector se resuelven aquí y no en cada XAML: si se escribieran en
/// la vista habría que escribirlos dos veces, una por interfaz, y acabarían divergiendo.
/// </summary>
public sealed class TimelineEventRow
{
    public TimelineEventRow(TimelineEvent source)
    {
        Event = source;
    }

    public TimelineEvent Event { get; }

    public Severity Severity => Event.Severity;
    public string Module => Event.Module;
    public string Kind => Event.Kind;
    public string Message => Event.Message;

    /// <summary>Día y hora con segundos: el orden dentro del mismo minuto es lo que se lee.</summary>
    public string TimeText => Event.Timestamp.LocalDateTime.ToString("dd/MM HH:mm:ss");

    public string SectorText => SectorNames.Display(Event.Sector);
}
