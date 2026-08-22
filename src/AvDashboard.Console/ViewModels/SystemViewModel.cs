using System.Collections.ObjectModel;
using AvDashboard.Console.Services;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Sistema (M1), partido en dos por una razón: son dos preguntas distintas y mezclarlas era
/// lo que hacía la página incomprensible.
///
/// «Estado» contesta a «¿cómo está esta máquina y hay algo que mirar?». Es diagnóstico: la
/// telemetría, lo que difiere del objetivo, lo que no se ha podido leer y los hechos que no
/// se configuran —un reinicio pendiente, los apagados inesperados del último trimestre—.
/// Se lee de arriba abajo y se cierra.
///
/// «Configuración» contesta a «¿qué puedo cambiar y dónde está?». Es el catálogo entero
/// agrupado, con su control en cada fila, esté bien o mal el valor actual. Se busca, se
/// toca y se sale.
///
/// Antes esto era una sola lista filtrada por defecto a lo que fallaba, y de ahí venían las
/// dos quejas a la vez: quien venía a ver cómo está la máquina tenía que leer una tabla de
/// ajustes, y quien venía a cambiar algo no lo encontraba porque el filtro lo escondía.
/// </summary>
public sealed partial class SystemViewModel : PageViewModel
{
    private SettingsAudit? _audit;

    public SystemViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Sistema";
    public override string Glyph => "\uE713";

    // ------------------------------------------------------------------ Telemetría

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _uptime = "—";

    [ObservableProperty]
    private string _powerPlan = "—";

    // ------------------------------------------------------------ Estado / Configuración

    /// <summary>
    /// Arranca en Estado. Es la pregunta que se hace uno al abrir la página, y desde ahí se
    /// llega a Configuración con un clic; al revés no: nadie abre los ajustes para
    /// enterarse de que hay un reinicio pendiente.
    /// </summary>
    [ObservableProperty]
    private bool _showSettings;

    /// <summary>Lo que difiere del objetivo, en rojo primero y con su control al lado.</summary>
    public ObservableCollection<SettingRow> Findings { get; } = new();

    /// <summary>
    /// Lo que no se ha podido leer. Va aparte y no en rojo: sin privilegios hay media docena
    /// que Windows no deja consultar, y pintarlas como fallo llena la pantalla de falsos
    /// positivos hasta que dejas de mirar los verdaderos.
    /// </summary>
    public ObservableCollection<SettingRow> Unreadable { get; } = new();

    /// <summary>
    /// Hechos, no ajustes: reinicio pendiente, apagados inesperados, versión de Windows. No
    /// tienen valor objetivo porque no hay nada que configurar, y por eso nunca deberían
    /// haber estado en la misma tabla que los ajustes.
    /// </summary>
    public ObservableCollection<SettingRow> Diagnostics { get; } = new();

    /// <summary>El catálogo entero, agrupado, para la pestaña de configuración.</summary>
    public ObservableCollection<SettingGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private string _summary = "Sin auditar.";

    [ObservableProperty]
    private string _stateHeadline = "Sin auditar.";

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _okCount;

    /// <summary>Cuántos ajustes escribibles hay, para que la pestaña no prometa de más.</summary>
    [ObservableProperty]
    private int _writableCount;

    [ObservableProperty]
    private int _catalogCount;

    /// <summary>
    /// Buscador de la pestaña de configuración. Sustituye a los dos filtros de casilla que
    /// había: escribir «energía» o «red» es más rápido que razonar qué esconde cada filtro,
    /// y sobre todo no esconde nada sin que lo hayas pedido.
    /// </summary>
    [ObservableProperty]
    private string _search = string.Empty;

    partial void OnSearchChanged(string value) => RebuildSettings();

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
            StateHeadline = Summary;
            Findings.Clear();
            Unreadable.Clear();
            Diagnostics.Clear();
            Groups.Clear();
            return;
        }

        _audit = audit.Value!;
        RebuildState();
        RebuildSettings();
    }

    /// <summary>La pestaña de estado: hallazgos, ilegibles y diagnóstico.</summary>
    private void RebuildState()
    {
        Findings.Clear();
        Unreadable.Clear();
        Diagnostics.Clear();

        if (_audit is not SettingsAudit audit)
        {
            return;
        }

        List<SettingEvaluation> all = audit.All.ToList();

        // Rojo antes que ámbar, y dentro de cada uno lo de impacto alto primero: es el
        // orden en el que se arreglan cuando quedan quince minutos.
        foreach (SettingEvaluation evaluation in all
                     .Where(e => e.IsDeviation)
                     .OrderByDescending(e => e.Verdict == SettingVerdict.DiffersCritical)
                     .ThenByDescending(e => e.Definition.Impact)
                     .ThenBy(e => e.Definition.Name, StringComparer.CurrentCulture))
        {
            Findings.Add(new SettingRow(evaluation));
        }

        foreach (SettingEvaluation evaluation in all
                     .Where(e => e.Verdict == SettingVerdict.Unknown)
                     .OrderBy(e => e.Definition.Name, StringComparer.CurrentCulture))
        {
            Unreadable.Add(new SettingRow(evaluation));
        }

        foreach (SettingEvaluation evaluation in all
                     .Where(e => e.Verdict == SettingVerdict.Informational)
                     .OrderBy(e => e.Definition.Name, StringComparer.CurrentCulture))
        {
            Diagnostics.Add(new SettingRow(evaluation));
        }

        CriticalCount = audit.CriticalCount;
        WarningCount = audit.WarningCount;
        OkCount = all.Count(e => e.Verdict == SettingVerdict.Match);
        CatalogCount = all.Count;
        WritableCount = all.Count(e => e.Writable);

        Health = audit.Health;

        StateHeadline = CriticalCount == 0 && WarningCount == 0
            ? "Nada que mirar. Todos los ajustes con objetivo coinciden."
            : CriticalCount == 0
                ? $"{WarningCount} conviene revisarlos antes del próximo show."
                : $"{CriticalCount} afectan a producción" +
                  (WarningCount > 0 ? $" y {WarningCount} más conviene revisarlos." : ".");

        Summary = StateHeadline;
    }

    /// <summary>La pestaña de configuración: el catálogo entero, filtrado solo si se busca.</summary>
    private void RebuildSettings()
    {
        Groups.Clear();

        if (_audit is not SettingsAudit audit)
        {
            return;
        }

        string needle = Search.Trim();

        foreach (SettingGroupSummary group in audit.Groups)
        {
            List<SettingRow> rows = group.Settings
                .Where(s => Matches(s, needle))
                .Select(s => new SettingRow(s))
                .ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            Groups.Add(new SettingGroupViewModel(group, rows));
        }
    }

    /// <summary>
    /// La búsqueda mira nombre, motivo y origen. El origen es lo que hace que buscar
    /// «powercfg» o el nombre de una clave del registro encuentre la fila, que es como se
    /// busca cuando vienes de leer un foro.
    /// </summary>
    private static bool Matches(SettingEvaluation evaluation, string needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        SettingDefinition definition = evaluation.Definition;

        return Contains(definition.Name, needle)
               || Contains(definition.Rationale, needle)
               || Contains(definition.Source, needle)
               || Contains(definition.TargetDescription, needle)
               || Contains(SettingsCatalog.DescribeGroup(definition.Group), needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null &&
        haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Lleva a la pestaña de configuración con el ajuste ya buscado.</summary>
    [RelayCommand]
    private void Locate(SettingRow? row)
    {
        if (row is null)
        {
            return;
        }

        Search = row.Name;
        ShowSettings = true;
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
        Group = summary.Group;
        Name = SettingsCatalog.DescribeGroup(summary.Group);
        Health = summary.Health;
        CriticalCount = summary.CriticalCount;
        WarningCount = summary.WarningCount;
        Rows = rows;
    }

    public SettingGroup Group { get; }
    public string Name { get; }
    public HealthState Health { get; }
    public int CriticalCount { get; }
    public int WarningCount { get; }
    public IReadOnlyList<SettingRow> Rows { get; }

    /// <summary>
    /// Icono del grupo. Windows 11 pone uno a la izquierda de cada sección de ajustes y no
    /// es decoración: es lo que te deja encontrar «Energía» sin leer, de un barrido.
    /// </summary>
    public string Glyph => Group switch
    {
        SettingGroup.Power => "\uE945",
        SettingGroup.Kernel => "\uEC4A",
        SettingGroup.Updates => "\uE895",
        SettingGroup.Security => "\uEA18",
        SettingGroup.Startup => "\uE7E8",
        SettingGroup.Network => "\uE839",
        SettingGroup.Diagnostics => "\uE9F9",
        _ => "\uE713",
    };

    /// <summary>«3 filas · 2 en rojo», que es lo que se lee con el grupo plegado.</summary>
    public string Counters
    {
        get
        {
            List<string> parts = new() { $"{Rows.Count} ajustes" };

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

    /// <summary>true si desde aquí se puede hacer algo. Lo contrario es informar y punto.</summary>
    public bool CanChange => CanToggle || CanApplyTarget;

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
    public string GroupName => SettingsCatalog.DescribeGroup(Evaluation.Definition.Group);

    public string Impact => SettingsCatalog.DescribeImpact(Evaluation.Definition.Impact);

    /// <summary>
    /// El motivo, siempre. En Windows 11 cada ajuste lleva su línea de explicación debajo y
    /// no solo los que están mal; enseñarlo solo en los que fallan lo convierte en un
    /// reproche en lugar de en documentación.
    /// </summary>
    public string Rationale => Evaluation.Definition.Rationale;

    /// <summary>«Debería ser: Ultimate Performance», para la lista de hallazgos.</summary>
    public string? TargetLine => Evaluation.Definition.TargetDescription is { Length: > 0 } target
        ? $"Debería ser: {target}"
        : null;

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
