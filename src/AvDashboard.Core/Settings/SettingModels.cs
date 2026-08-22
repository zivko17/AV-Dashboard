using System.Globalization;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Settings;

/// <summary>Grupo del catálogo. Coincide con las secciones plegables de la pestaña Sistema.</summary>
public enum SettingGroup
{
    /// <summary>Plan de energía y todo lo que puede dormir un dispositivo a media función.</summary>
    Power,

    /// <summary>Planificador, MMCSS, efectos visuales: lo que decide cuánta CPU llega a tiempo.</summary>
    Kernel,

    /// <summary>Actualizaciones, notificaciones y todo lo que interrumpe sin avisar.</summary>
    Updates,

    /// <summary>Defender y protecciones. Casi todo aquí es lectura o compromiso explícito.</summary>
    Security,

    /// <summary>Arranque y sesión.</summary>
    Startup,

    /// <summary>
    /// Lo de la NIC que no es una dirección IP: descargas de proceso, moderación de
    /// interrupciones, ahorro de energía. Es donde se rompe la temporización de Dante.
    /// </summary>
    Network,

    /// <summary>Solo lectura, sin objetivo: contexto para entender por qué algo va mal.</summary>
    Diagnostics,
}

/// <summary>
/// Cuánto mueve la aguja de verdad. No es lo mismo que gravedad: un ajuste de impacto bajo
/// mal puesto sigue estando mal, pero con quince minutos antes del show no es donde miras.
/// </summary>
public enum SettingImpact
{
    Low,
    Medium,
    High,
}

/// <summary>Contrapartida de cambiar un ajuste.</summary>
public enum SettingRisk
{
    /// <summary>Ninguna: el valor objetivo es el correcto en cualquier rig.</summary>
    None,

    /// <summary>
    /// Depende del equipo y hay que medirlo. HAGS es el caso claro: en unos rigs mejora
    /// NVENC y en otros mete microcortes. Se muestra el estado, no se impone un objetivo.
    /// </summary>
    Tradeoff,

    /// <summary>
    /// Cambiarlo reduce la seguridad de la máquina de forma real. Nunca entra en un perfil
    /// que se aplica de un clic: se enseña, se explica la contrapartida y se confirma a mano.
    /// </summary>
    Security,
}

/// <summary>Resultado de comparar lo que hay con lo que debería haber.</summary>
public enum SettingVerdict
{
    /// <summary>No se ha podido leer. No es lo mismo que estar mal.</summary>
    Unknown,

    /// <summary>Sin objetivo definido: se muestra el valor y ya.</summary>
    Informational,

    /// <summary>Coincide con el objetivo.</summary>
    Match,

    /// <summary>Difiere, pero no es de los que tumban un show.</summary>
    Differs,

    /// <summary>Difiere y afecta a producción.</summary>
    DiffersCritical,
}

/// <summary>
/// Una fila del catálogo: qué es, de dónde sale, qué valor queremos en un rig y por qué.
///
/// El «por qué» no es adorno. Un catálogo de cuarenta ajustes sin explicación es una lista
/// de casillas que se marcan sin pensar, y algunas de estas casillas tienen consecuencias.
/// </summary>
public sealed record SettingDefinition
{
    public required string Id { get; init; }
    public required SettingGroup Group { get; init; }
    public required string Name { get; init; }

    /// <summary>De dónde se lee: clave de registro, GUID de powercfg, WMI…</summary>
    public required string Source { get; init; }

    /// <summary>
    /// Valores que se consideran correctos en un rig de producción. Vacío significa que el
    /// ajuste es informativo y nunca se pinta en rojo.
    /// </summary>
    public IReadOnlyList<string> AcceptableValues { get; init; } = Array.Empty<string>();

    /// <summary>El objetivo en lenguaje humano: «Desactivado», «100 %», «Nunca».</summary>
    public string? TargetDescription { get; init; }

    public SettingImpact Impact { get; init; } = SettingImpact.Medium;
    public SettingRisk Risk { get; init; } = SettingRisk.None;

    /// <summary>Una línea: por qué importa esto en un rig de directo.</summary>
    public required string Rationale { get; init; }

    /// <summary>Identificador del actuador que lo cambia, cuando exista.</summary>
    public string? ActionId { get; init; }

    /// <summary>true si el ajuste tiene objetivo y por tanto puede estar «mal».</summary>
    public bool HasTarget => AcceptableValues.Count > 0;

    /// <summary>
    /// Los de riesgo de seguridad quedan fuera de cualquier perfil automático, tengan o no
    /// actuador. Es una propiedad del ajuste, no una decisión de la UI.
    /// </summary>
    public bool ExcludedFromProfiles => Risk == SettingRisk.Security;
}

/// <summary>
/// Lo leído para un ajuste concreto.
///
/// El valor viaja como cadena canónica y no como <c>object</c> a propósito: el catálogo
/// compara contra cadenas, la API lo serializa sin ceremonia, y los helpers estáticos son
/// los únicos que saben dar formato, con lo que la capa nativa no puede desviarse.
/// </summary>
public sealed record SettingReading
{
    public required string Id { get; init; }

    /// <summary>Valor canónico, o null si no se ha podido leer.</summary>
    public string? Value { get; init; }

    /// <summary>Texto para la vista cuando el valor canónico no se lee bien de un vistazo.</summary>
    public string? Display { get; init; }

    /// <summary>Detalle de por qué no se ha podido leer, si es el caso.</summary>
    public string? Unavailable { get; init; }

    public static SettingReading Of(string id, bool? value, string? display = null) => new()
    {
        Id = id,
        Value = value is null ? null : value.Value ? BooleanOn : BooleanOff,
        Display = display,
    };

    public static SettingReading Of(string id, int? value, string? display = null) => new()
    {
        Id = id,
        Value = value?.ToString(CultureInfo.InvariantCulture),
        Display = display,
    };

    public static SettingReading Of(string id, string? value, string? display = null) => new()
    {
        Id = id,
        Value = string.IsNullOrWhiteSpace(value) ? null : value,
        Display = display,
    };

    /// <summary>
    /// Metodo aparte y no sobrecarga: un literal entero convierte a int? y a uint? a la vez,
    /// con lo que <c>Of(id, 10)</c> seria ambiguo. El caso real es NetworkThrottlingIndex,
    /// que vale 0xFFFFFFFF y no cabe en un int con signo.
    /// </summary>
    public static SettingReading OfUnsigned(string id, uint? value, string? display = null) => new()
    {
        Id = id,
        Value = value?.ToString(CultureInfo.InvariantCulture),
        Display = display,
    };

    public static SettingReading Missing(string id, string reason) => new()
    {
        Id = id,
        Unavailable = reason,
    };

    public const string BooleanOn = "on";
    public const string BooleanOff = "off";
}

/// <summary>Una fila del catálogo con su lectura y su veredicto.</summary>
public sealed record SettingEvaluation
{
    public required SettingDefinition Definition { get; init; }
    public required SettingVerdict Verdict { get; init; }

    public string? Current { get; init; }
    public string? Display { get; init; }
    public string? Unavailable { get; init; }

    /// <summary>Semáforo, con la convención del catálogo: ámbar «revísalo», rojo «está roto».</summary>
    public HealthState Health => Verdict switch
    {
        SettingVerdict.Match => HealthState.Ok,
        SettingVerdict.Differs => HealthState.Warning,
        SettingVerdict.DiffersCritical => HealthState.Critical,
        _ => HealthState.Unknown,
    };

    /// <summary>
    /// true si este nodo sabe escribir el ajuste.
    ///
    /// Lo decide la capa nativa y no el catálogo: así una fila nunca ofrece un interruptor
    /// que luego no hace nada, ni al revés.
    /// </summary>
    public bool Writable { get; init; }

    /// <summary>
    /// true cuando el ajuste es un sí o no y la vista puede pintar un interruptor. Los que
    /// tienen valor numérico o un plan con nombre necesitan otro control.
    /// </summary>
    public bool IsToggle =>
        Definition.AcceptableValues.Count == 1 &&
        (Definition.AcceptableValues[0] == SettingReading.BooleanOn ||
         Definition.AcceptableValues[0] == SettingReading.BooleanOff);

    /// <summary>Estado del interruptor. null cuando no se ha podido leer.</summary>
    public bool? ToggleState => Current switch
    {
        SettingReading.BooleanOn => true,
        SettingReading.BooleanOff => false,
        _ => null,
    };

    /// <summary>true si esta fila sobrevive al filtro «solo lo que difiere del objetivo».</summary>
    public bool IsDeviation => Verdict is SettingVerdict.Differs or SettingVerdict.DiffersCritical;

    /// <summary>Lo que se pinta en la columna de valor.</summary>
    public string CurrentText => Display ?? Current ?? Unavailable ?? "sin leer";
}

/// <summary>Resumen por grupo, para la cabecera plegada.</summary>
public sealed record SettingGroupSummary
{
    public required SettingGroup Group { get; init; }
    public required IReadOnlyList<SettingEvaluation> Settings { get; init; }

    public int CriticalCount => Settings.Count(s => s.Verdict == SettingVerdict.DiffersCritical);
    public int WarningCount => Settings.Count(s => s.Verdict == SettingVerdict.Differs);
    public int UnknownCount => Settings.Count(s => s.Verdict == SettingVerdict.Unknown);

    public HealthState Health =>
        CriticalCount > 0 ? HealthState.Critical
        : WarningCount > 0 ? HealthState.Warning
        : Settings.Any(s => s.Verdict == SettingVerdict.Match) ? HealthState.Ok
        : HealthState.Unknown;
}

/// <summary>Auditoría completa del nodo contra el catálogo.</summary>
public sealed record SettingsAudit
{
    public DateTimeOffset TakenAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SettingGroupSummary> Groups { get; init; } = Array.Empty<SettingGroupSummary>();

    public IEnumerable<SettingEvaluation> All => Groups.SelectMany(g => g.Settings);

    public IEnumerable<SettingEvaluation> Deviations => All.Where(s => s.IsDeviation);

    public int CriticalCount => Groups.Sum(g => g.CriticalCount);
    public int WarningCount => Groups.Sum(g => g.WarningCount);

    public HealthState Health =>
        CriticalCount > 0 ? HealthState.Critical
        : WarningCount > 0 ? HealthState.Warning
        : HealthState.Ok;
}
