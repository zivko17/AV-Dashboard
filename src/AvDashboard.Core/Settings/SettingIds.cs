namespace AvDashboard.Core.Settings;

/// <summary>
/// Identificadores estables de las filas del catálogo.
///
/// Igual que <see cref="Profiles.ActionIds"/>: una auditoría guardada o un perfil que
/// referencie una fila lo hace por esta cadena, así que se añaden y no se renombran.
/// </summary>
public static class SettingIds
{
    // --- Energía ---
    public const string PowerPlan = "power.plan";
    public const string UltimateAvailable = "power.ultimate-available";
    public const string ProcessorMin = "power.processor-min";
    public const string ProcessorMax = "power.processor-max";
    public const string CoreParking = "power.core-parking";
    public const string UsbSelectiveSuspend = "power.usb-selective-suspend";
    public const string PcieAspm = "power.pcie-aspm";
    public const string DiskTimeout = "power.disk-timeout";
    public const string SleepTimeout = "power.sleep-timeout";
    public const string DisplayTimeout = "power.display-timeout";
    public const string Hibernation = "power.hibernation";
    public const string WakeTimers = "power.wake-timers";

    /// <summary>Tareas programadas con «despertar el equipo» armado.</summary>
    public const string WakeArmedTasks = "power.wake-armed-tasks";

    // --- Kernel y rendimiento ---
    public const string MmcssResponsiveness = "kernel.mmcss-responsiveness";
    public const string NetworkThrottling = "kernel.network-throttling";
    public const string ProAudioPriority = "kernel.pro-audio-priority";
    public const string PrioritySeparation = "kernel.priority-separation";
    public const string PowerThrottling = "kernel.power-throttling";
    public const string VisualEffects = "kernel.visual-effects";
    public const string Transparency = "kernel.transparency";
    public const string Hags = "kernel.hags";
    public const string GameMode = "kernel.game-mode";
    public const string GameDvr = "kernel.game-dvr";
    public const string BackgroundApps = "kernel.background-apps";
    public const string MemoryIntegrity = "kernel.memory-integrity";
    public const string CpuMitigations = "kernel.cpu-mitigations";

    /// <summary>Archivo de paginacion de tamano fijo: el que no crece a mitad de show.</summary>
    public const string PagefileFixed = "kernel.pagefile-fixed";

    // --- Sincronia de hora ---

    /// <summary>El servicio de hora arrancando solo, y no a golpe de desencadenador.</summary>
    public const string TimeServiceStartup = "time.service-startup";

    /// <summary>Cuando se puso el reloj en hora por ultima vez. Es el resultado.</summary>
    public const string TimeLastSync = "time.last-sync";

    /// <summary>De donde sale la hora. Informativo: depende del montaje.</summary>
    public const string TimeSource = "time.source";

    // --- Interrupciones y actualizaciones ---
    public const string UpdatePaused = "updates.paused";
    public const string RebootPending = "updates.reboot-pending";
    public const string AutoRebootBlocked = "updates.auto-reboot-blocked";
    public const string DriversViaUpdate = "updates.drivers";
    public const string DeliveryOptimization = "updates.delivery-optimization";
    public const string FocusAssist = "updates.focus-assist";
    public const string Notifications = "updates.notifications";
    public const string StoreAutoUpdate = "updates.store-auto-update";
    public const string StorageSense = "updates.storage-sense";
    public const string CoInstallers = "updates.co-installers";
    public const string SearchIndexing = "updates.search-indexing";

    // --- Seguridad ---
    public const string TamperProtection = "security.tamper-protection";
    public const string RealTimeProtection = "security.real-time-protection";
    public const string ControlledFolderAccess = "security.controlled-folder-access";

    // --- Arranque y sesión ---
    public const string FastStartup = "startup.fast-startup";
    public const string ScreenSaver = "startup.screen-saver";
    public const string InactivityLock = "startup.inactivity-lock";
    public const string RestartApps = "startup.restart-apps";

    // --- Diagnóstico ---
    public const string UnexpectedShutdowns = "diagnostics.unexpected-shutdowns";
    public const string DevicesWithError = "diagnostics.devices-with-error";
    public const string TimerResolution = "diagnostics.timer-resolution";
    public const string VirtualizationFirmware = "diagnostics.virtualization-firmware";

    // --- Red ---

    /// <summary>Descargas de proceso y ahorro de energia en la NIC.</summary>
    public const string NicOffloads = "network.offloads";

    /// <summary>Ahorro de energia del adaptador: Windows puede apagarlo para ahorrar.</summary>
    public const string NicPowerManagement = "network.nic-power-management";

    /// <summary>Nagle: agrupa paquetes pequenos y anade latencia a los protocolos de control.</summary>
    public const string NicNagle = "network.nic-nagle";

    /// <summary>Las aplicaciones de produccion corriendo en la prioridad que les toca.</summary>
    public const string ProductionPriority = "system.production-priority";

    /// <summary>Optimizaciones de pantalla completa: cambian como se presenta cada frame.</summary>
    public const string FullscreenOptimizations = "video.fullscreen-optimizations";

    /// <summary>Frecuencia de actualizacion variable. En un servidor de medios, apagada.</summary>
    public const string VariableRefreshRate = "video.variable-refresh-rate";
}
