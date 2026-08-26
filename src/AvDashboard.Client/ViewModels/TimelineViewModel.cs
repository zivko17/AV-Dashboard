using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;
using AvDashboard.Core.Taxonomy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            Events.Add(new TimelineEventRow(item, UndoAsync));
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

    /// <summary>
    /// Deshace la escritura de esta linea, ensenando antes lo que va a pasar.
    ///
    /// Se calcula en seco contra el agente y se confirma con el resultado real, no con lo
    /// que el cliente crea: si el ajuste ya no vale lo que dejo ese cambio —porque alguien
    /// lo toco despues— el agente se niega y el motivo se lee aqui, antes de aceptar nada.
    /// </summary>
    private async Task UndoAsync(TimelineEventRow row)
    {
        row.IsBusy = true;

        try
        {
            ApiResult<SettingWriteResponse> preview = await Nodes.Api()
                .UndoAsync(row.Event.Id, dryRun: true).ConfigureAwait(true);

            if (!preview.Success || preview.Value is null)
            {
                Summary = preview.Error ?? "No se puede deshacer esa línea.";
                return;
            }

            bool go = await Shell.ConfirmAsync(
                "Deshacer este cambio",
                $"{row.Message}" + Environment.NewLine + Environment.NewLine +
                $"Volvería a: {preview.Value.AppliedValue}." + Environment.NewLine +
                "Queda anotado en el Timeline como deshecho.",
                "Deshacer").ConfigureAwait(true);

            if (!go)
            {
                Summary = "Cancelado. No se ha tocado nada.";
                return;
            }

            ApiResult<SettingWriteResponse> result = await Nodes.Api()
                .UndoAsync(row.Event.Id).ConfigureAwait(true);

            Summary = result.Success && result.Value is not null
                ? $"Deshecho: {result.Value.PreviousValue ?? "sin valor"} → {result.Value.AppliedValue}"
                : result.Error ?? "No se ha podido deshacer.";
        }
        finally
        {
            row.IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
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
public sealed partial class TimelineEventRow : ObservableObject
{
    private readonly Func<TimelineEventRow, Task>? _undo;

    public TimelineEventRow(TimelineEvent source, Func<TimelineEventRow, Task>? undo = null)
    {
        Event = source;
        _undo = undo;
    }

    public TimelineEvent Event { get; }

    public Severity Severity => Event.Severity;
    public string Module => Event.Module;
    public string Kind => Event.Kind;
    public string Message => Event.Message;

    /// <summary>Día y hora con segundos: el orden dentro del mismo minuto es lo que se lee.</summary>
    public string TimeText => Event.Timestamp.LocalDateTime.ToString("dd/MM HH:mm:ss");

    public string SectorText => SectorNames.Display(Event.Sector);

    /// <summary>
    /// Si esta linea es una escritura que cambio algo, y por tanto tiene sentido ofrecer
    /// deshacerla.
    ///
    /// Es una condicion optimista y no la verdad: quien decide de verdad es el agente, que
    /// mira ademas si el ajuste sigue existiendo, si sabe escribirlo y —sobre todo— si
    /// alguien lo ha tocado despues. Replicar aqui esas cuatro comprobaciones seria
    /// escribirlas dos veces y verlas divergir; lo que se replica es solo lo que evita
    /// ofrecer un boton en una linea de ping, que no tiene nada que ver.
    /// </summary>
    public bool CanAttemptUndo =>
        _undo is not null && string.Equals(Kind, SettingUndo.ChangedKind, StringComparison.Ordinal);

    /// <summary>Mientras se calcula o se aplica, para que no se pulse dos veces.</summary>
    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private Task UndoAsync() => _undo is null ? Task.CompletedTask : _undo(this);
}
