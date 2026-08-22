namespace AvDashboard.Core.Profiles;

/// <summary>
/// Identificadores estables de los actuadores.
///
/// Un perfil guardado en SQLite referencia acciones por esta cadena, así que cambiar una
/// rompe los perfiles ya escritos. Se añaden, no se renombran.
///
/// Core los declara aunque no implemente ninguno: son el contrato contra el que
/// AvDashboard.Native escribirá los actuadores, y lo que permite escribir perfiles y
/// probarlos antes de que ese código exista.
/// </summary>
public static class ActionIds
{
    // --- M1 Sistema ---
    public const string PowerPlan = "system.power-plan";
    public const string ProcessorState = "system.processor-state";
    public const string FastStartup = "system.fast-startup";
    public const string VisualEffects = "system.visual-effects";
    public const string MmcssResponsiveness = "system.mmcss-responsiveness";
    public const string NetworkThrottling = "system.network-throttling";
    public const string UsbSelectiveSuspend = "system.usb-selective-suspend";
    public const string GameBar = "system.game-bar";
    public const string FocusAssist = "system.focus-assist";
    public const string WindowsUpdatePause = "system.windows-update-pause";
    public const string ProcessPriority = "system.process-priority";

    // --- M2 Firewall ---
    public const string FirewallProfile = "firewall.profile";
    public const string FirewallAvPreset = "firewall.av-preset";

    // --- M3 Audio ---
    public const string AudioDefaultDevice = "audio.default-device";
    public const string AudioSessionRoute = "audio.session-route";

    // --- M4 Vídeo ---
    public const string DisplayLayoutRestore = "video.display-layout";
    public const string WindowLayoutRestore = "video.window-layout";

    // --- M5 Red ---
    public const string NetworkProfile = "network.profile";
    public const string NicPowerManagement = "network.nic-power-management";
    public const string NicNagle = "network.nic-nagle";
    public const string NdiDiscovery = "network.ndi-discovery";

    // --- M6 Servicios ---
    public const string ServiceState = "services.state";
    public const string ServiceStartMode = "services.start-mode";

    /// <summary>Todos los identificadores conocidos. Lo usan las pruebas y el validador.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PowerPlan, ProcessorState, FastStartup, VisualEffects, MmcssResponsiveness,
        NetworkThrottling, UsbSelectiveSuspend, GameBar, FocusAssist, WindowsUpdatePause,
        ProcessPriority,
        FirewallProfile, FirewallAvPreset,
        AudioDefaultDevice, AudioSessionRoute,
        DisplayLayoutRestore, WindowLayoutRestore,
        NetworkProfile, NicPowerManagement, NicNagle, NdiDiscovery,
        ServiceState, ServiceStartMode,
    };

    /// <summary>
    /// Acciones que pueden interrumpir una salida en directo. El guardarraíl de show
    /// activo mira esta lista antes de dejar aplicar un perfil sin confirmación extra.
    /// </summary>
    public static IReadOnlySet<string> Disruptive { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AudioDefaultDevice,
        AudioSessionRoute,
        DisplayLayoutRestore,
        WindowLayoutRestore,
        NetworkProfile,
        NicPowerManagement,
        NdiDiscovery,
        ServiceState,
        FirewallProfile,
    };

    public static bool IsDisruptive(string actionId) => Disruptive.Contains(actionId);
}
