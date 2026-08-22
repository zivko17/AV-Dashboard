using AvDashboard.Core.Models;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Perfiles de partida.
///
/// No son ejemplos de documentación: son los perfiles que se cargan la primera vez que
/// arranca el agente, para que el módulo que da valor esté ahí desde el primer día en
/// lugar de empezar con una lista vacía.
///
/// Están en Core y no en el agente porque son datos, se pueden probar sin Windows, y
/// definen implícitamente qué actuadores hay que escribir en la Fase 2.
/// </summary>
public static class ProfileLibrary
{
    /// <summary>
    /// «Evento — Rig Portátil». Es el perfil del documento de arquitectura, tal cual.
    ///
    /// El orden importa y no es alfabético: primero lo que no interrumpe nada, después la
    /// red, y al final los servicios y las aplicaciones, que son lo que se nota si algo
    /// va mal a mitad de aplicación.
    /// </summary>
    public static ProfileDefinition EventoRigPortatil { get; } = new()
    {
        Id = "evento-rig-portatil",
        Name = "Evento — Rig Portátil",
        Description =
            "Deja la máquina lista para producir: energía a tope, sin ahorros que corten " +
            "Dante o NDI, y sin nada de Windows queriendo actualizarse a mitad de show.",
        Steps = new[]
        {
            Step(0, ActionIds.PowerPlan, new PowerPlanParameters { Ultimate = true },
                "Plan de energía → Ultimate Performance"),

            Step(1, ActionIds.VisualEffects,
                new VisualEffectsParameters { Setting = VisualEffectsSetting.BestPerformance },
                "Efectos visuales → Mejor rendimiento"),

            Step(2, ActionIds.FastStartup, new BooleanSettingParameters { Enabled = false },
                "Fast Startup → desactivado"),

            Step(3, ActionIds.MmcssResponsiveness, new NumericSettingParameters { Value = 10 },
                "MMCSS SystemResponsiveness → 10"),

            Step(4, ActionIds.NetworkThrottling, new NumericSettingParameters { Value = 0xFFFFFFFF },
                "NetworkThrottlingIndex → 0xFFFFFFFF"),

            Step(5, ActionIds.UsbSelectiveSuspend, new BooleanSettingParameters { Enabled = false },
                "USB selective suspend → desactivado"),

            Step(6, ActionIds.WindowsUpdatePause, new WindowsUpdatePauseParameters { Days = 7 },
                "Windows Update → pausado 7 días"),

            Step(7, ActionIds.FocusAssist, new FocusAssistParameters { Mode = FocusAssistMode.AlarmsOnly },
                "Focus Assist → solo alarmas"),

            Step(8, ActionIds.GameBar, new BooleanSettingParameters { Enabled = false },
                "Game Bar → desactivada"),

            // Red. A partir de aquí sí se puede notar, así que va después de lo inocuo.
            Step(9, ActionIds.NicPowerManagement, new NicSettingParameters { Enabled = false },
                "Power management de NIC → desactivado en todas"),

            Step(10, ActionIds.NicNagle, new NicSettingParameters { Enabled = false },
                "Nagle → desactivado"),

            Step(11, ActionIds.FirewallProfile, new FirewallProfileParameters { Enabled = true },
                "Firewall → activado en los tres perfiles"),

            Step(12, ActionIds.FirewallAvPreset, new FirewallPresetParameters(),
                "Firewall → preset AV Ports"),

            // Servicios y aplicaciones al final: es lo que se nota si falla.
            Step(13, ActionIds.ServiceState, new ServiceStateParameters
            {
                Services = new[] { "Dante Virtual Soundcard", "Dante Discovery", "NDI Discovery Service", "Tailscale" },
                Running = true,
            }, "Servicios AV → arrancados", continueOnError: true),

            Step(14, ActionIds.ProcessPriority, new ProcessPriorityParameters
            {
                ImageName = "vMix64.exe",
                Priority = ProcessPriority.High,
            }, "vMix64.exe → prioridad alta", continueOnError: true),
        },
    };

    /// <summary>
    /// «Oficina». Es lo contrario, escrito a mano y no generado: el inverso automático
    /// devuelve la máquina al estado exacto que tenía, y a veces lo que quieres es
    /// devolverla a un estado razonable aunque no fuera el suyo.
    /// </summary>
    public static ProfileDefinition Oficina { get; } = new()
    {
        Id = "oficina",
        Name = "Oficina",
        Description = "Devuelve la máquina a un comportamiento normal de escritorio.",
        Steps = new[]
        {
            Step(0, ActionIds.ProcessPriority, new ProcessPriorityParameters
            {
                ImageName = "vMix64.exe",
                Priority = ProcessPriority.Normal,
            }, "vMix64.exe → prioridad normal", continueOnError: true),

            Step(1, ActionIds.ServiceState, new ServiceStateParameters
            {
                Services = new[] { "Dante Virtual Soundcard" },
                Running = false,
            }, "Dante Virtual Soundcard → parado", continueOnError: true),

            Step(2, ActionIds.FocusAssist, new FocusAssistParameters { Mode = FocusAssistMode.Off },
                "Focus Assist → desactivado"),

            Step(3, ActionIds.VisualEffects,
                new VisualEffectsParameters { Setting = VisualEffectsSetting.LetWindowsChoose },
                "Efectos visuales → los que decida Windows"),

            Step(4, ActionIds.PowerPlan,
                new PowerPlanParameters { SchemeGuid = BalancedScheme },
                "Plan de energía → Equilibrado"),
        },
    };

    /// <summary>
    /// «Diagnóstico». No cambia nada: existe para tener un perfil con el que probar el
    /// motor, el dry-run y el rollback en una máquina real sin arriesgar nada.
    /// </summary>
    public static ProfileDefinition Diagnostico { get; } = new()
    {
        Id = "diagnostico",
        Name = "Diagnóstico (no cambia nada)",
        Description =
            "Perfil vacío para verificar el motor de perfiles en una máquina de verdad: " +
            "toma snapshot, calcula el diff y no toca nada.",
        Steps = Array.Empty<ProfileStep>(),
    };

    public static IReadOnlyList<ProfileDefinition> All { get; } = new[]
    {
        EventoRigPortatil,
        Oficina,
        Diagnostico,
    };

    /// <summary>GUID del esquema Equilibrado, fijo y documentado por Microsoft.</summary>
    private static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private static ProfileStep Step<T>(
        int order,
        string actionId,
        T parameters,
        string label,
        bool continueOnError = false) => new()
    {
        ActionId = actionId,
        ParametersJson = ActionParameters.Serialize(parameters),
        Label = label,
        Order = order,
        ContinueOnError = continueOnError,
    };
}
