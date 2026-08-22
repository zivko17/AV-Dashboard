using System.Collections.ObjectModel;
using AvDashboard.Console.Services;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Sistema (M1). Estado antes que control: primero lo que está pasando, y debajo el
/// catálogo de ajustes con su valor actual y el que espera un rig de producción.
///
/// Cuarenta y dos filas en una lista plana no se leen, así que la vista arranca filtrada a
/// lo que difiere del objetivo. Ese es el modo de día de show: enseña las cinco cosas que
/// hay que mirar, no las cuarenta y dos que están bien.
/// </summary>
public sealed partial class SystemViewModel : PageViewModel
{
    private SettingsAudit? _audit;

    public SystemViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Sistema";
    public override string Glyph => "\uE713";

    public ObservableCollection<SettingGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _uptime = "—";

    [ObservableProperty]
    private string _powerPlan = "—";

    /// <summary>El filtro por defecto, y el que de verdad se usa.</summary>
    [ObservableProperty]
    private bool _onlyDeviations = true;

    /// <summary>Con quince minutos arreglas los de impacto alto y te olvidas del resto.</summary>
    [ObservableProperty]
    private bool _onlyHighImpact;

    [ObservableProperty]
    private string _summary = "Sin auditar.";

    partial void OnOnlyDeviationsChanged(bool value) => Rebuild();

    partial void OnOnlyHighImpactChanged(bool value) => Rebuild();

    protected override async Task LoadAsync()
    {
        ApiClient api = Nodes.Api();

        ApiResult<SystemResponse> result = await api.GetSystemAsync().ConfigureAwait(true);
        if (!Apply(result, out SystemResponse system))
        {
            return;
        }

        CpuPercent = system.Status.CpuTotalPercent;
        MemoryPercent = system.Status.MemoryUsedPercent;
        Uptime = FormatUptime(system.Status.Uptime);
        PowerPlan = system.Settings.ActivePowerPlanName ?? "desconocido";

        // La auditoría va en una llamada aparte porque es cara: toca WMI y el registro de
        // eventos. La cabecera de telemetría no tiene por qué esperar por ella.
        ApiResult<SettingsAudit> audit = await api.GetSystemAuditAsync().ConfigureAwait(true);

        if (!audit.Success)
        {
            Summary = audit.Error ?? "No se ha podido auditar el sistema.";
            Groups.Clear();
            return;
        }

        _audit = audit.Value!;
        Rebuild();
    }

    private void Rebuild()
    {
        Groups.Clear();

        if (_audit is not SettingsAudit audit)
        {
            return;
        }

        foreach (SettingGroupSummary group in audit.Groups)
        {
            List<SettingRow> rows = group.Settings
                .Where(Passes)
                .Select(s => new SettingRow(s))
                .ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            Groups.Add(new SettingGroupViewModel(group, rows));
        }

        Health = audit.Health;

        Summary = audit.CriticalCount == 0 && audit.WarningCount == 0
            ? "Ningún ajuste difiere del objetivo."
            : $"{audit.CriticalCount} afectan a producción, {audit.WarningCount} conviene revisarlos.";

        if (Groups.Count == 0 && OnlyDeviations)
        {
            Summary += "  Nada que mostrar con el filtro puesto.";
        }
    }

    private bool Passes(SettingEvaluation evaluation)
    {
        if (OnlyDeviations && !evaluation.IsDeviation)
        {
            return false;
        }

        return !OnlyHighImpact || evaluation.Definition.Impact == SettingImpact.High;
    }

    /// <summary>
    /// Aplica el valor contrario al que tiene ahora. Es lo que hace el interruptor.
    ///
    /// Después no se toca la fila a mano: se vuelve a pedir la auditoría entera. Si el
    /// cambio no ha surtido efecto —falta de permisos, una directiva que lo pisa— la fila
    /// vuelve sola a su sitio en lugar de quedarse mintiendo.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAsync(SettingRow? row)
    {
        if (row is null || !row.CanToggle)
        {
            return;
        }

        string target = row.IsOn == true ? SettingReading.BooleanOff : SettingReading.BooleanOn;

        await ApplyAsync(row, target).ConfigureAwait(true);
    }

    /// <summary>Lleva la fila a su valor objetivo. Para las que no son un sí o un no.</summary>
    [RelayCommand]
    private async Task ApplyTargetAsync(SettingRow? row)
    {
        if (row?.Evaluation.Definition.AcceptableValues is not { Count: > 0 } acceptable)
        {
            return;
        }

        await ApplyAsync(row, acceptable[0]).ConfigureAwait(true);
    }

    private async Task ApplyAsync(SettingRow row, string value)
    {
        row.IsBusy = true;

        try
        {
            ApiResult<SettingWriteResponse> result = await Nodes.Api()
                .ApplySettingAsync(row.Evaluation.Definition.Id, value)
                .ConfigureAwait(true);

            Summary = result.Success
                ? $"{row.Name}: {result.Value!.PreviousValue ?? "sin valor"} → {result.Value.AppliedValue}"
                : result.Error ?? "No se ha podido aplicar el cambio.";
        }
        finally
        {
            row.IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private static string FormatUptime(TimeSpan uptime) =>
        $"{(int)uptime.TotalDays} d {uptime.Hours} h {uptime.Minutes} min";
}

/// <summary>Un grupo del catálogo con su contador, para la cabecera plegable.</summary>
public sealed class SettingGroupViewModel
{
    public SettingGroupViewModel(SettingGroupSummary summary, IReadOnlyList<SettingRow> rows)
    {
        Name = SettingsCatalog.DescribeGroup(summary.Group).ToUpperInvariant();
        Health = summary.Health;
        CriticalCount = summary.CriticalCount;
        WarningCount = summary.WarningCount;
        Rows = rows;
    }

    public string Name { get; }
    public HealthState Health { get; }
    public int CriticalCount { get; }
    public int WarningCount { get; }
    public IReadOnlyList<SettingRow> Rows { get; }

    /// <summary>«3 en rojo · 2 en ámbar», o vacío si el grupo está limpio.</summary>
    public string Counters
    {
        get
        {
            List<string> parts = new();

            if (CriticalCount > 0)
            {
                parts.Add($"{CriticalCount} en rojo");
            }

            if (WarningCount > 0)
            {
                parts.Add($"{WarningCount} en ámbar");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Una fila del catálogo tal y como se pinta.</summary>
public sealed partial class SettingRow : ObservableObject
{
    public SettingRow(SettingEvaluation evaluation)
    {
        Evaluation = evaluation;
    }

    public SettingEvaluation Evaluation { get; }

    /// <summary>Mientras se escribe, el control se bloquea para no encadenar clics.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>true si la fila se pinta con interruptor.</summary>
    public bool CanToggle => Evaluation.Writable && Evaluation.IsToggle;

    /// <summary>true si la fila se pinta con un botón «llevar al objetivo».</summary>
    public bool CanApplyTarget =>
        Evaluation.Writable && !Evaluation.IsToggle && Evaluation.Definition.HasTarget;

    public bool? IsOn => Evaluation.ToggleState;

    /// <summary>
    /// Por qué esta fila no tiene control. Se dice en lugar de dejar un hueco: «no se puede
    /// tocar» y «no me he molestado» se ven igual si nadie lo explica.
    /// </summary>
    public string? NotWritableReason
    {
        get
        {
            if (Evaluation.Writable || !Evaluation.Definition.HasTarget)
            {
                return null;
            }

            return Evaluation.Definition.Risk == SettingRisk.Security
                ? "A mano: cambiarlo reduce la seguridad de la máquina."
                : "Solo lectura en esta versión.";
        }
    }

    public string Name => Evaluation.Definition.Name;
    public string Current => Evaluation.CurrentText;
    public string Target => Evaluation.Definition.TargetDescription ?? "—";
    public string Source => Evaluation.Definition.Source;
    public HealthState Health => Evaluation.Health;

    public string Impact => SettingsCatalog.DescribeImpact(Evaluation.Definition.Impact);

    /// <summary>
    /// El motivo solo se pinta en lo que está mal. En una fila correcta sería ruido, y con
    /// cuarenta filas el ruido es lo que hace que dejes de leer la pantalla.
    /// </summary>
    public string? Rationale => Evaluation.IsDeviation ? Evaluation.Definition.Rationale : null;

    /// <summary>
    /// Aviso de los que no son un interruptor normal: bajarlos reduce la seguridad de la
    /// máquina de verdad, así que nunca entran en un perfil que se aplica de un clic.
    /// </summary>
    public string? Warning => Evaluation.Definition.Risk switch
    {
        SettingRisk.Security =>
            "Fuera de los perfiles automáticos: bajarlo reduce la seguridad de la máquina.",
        SettingRisk.Tradeoff =>
            "Sin respuesta universal: hay que probarlo y medirlo en este rig.",
        _ => null,
    };
}
