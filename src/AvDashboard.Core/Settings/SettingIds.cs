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
    public const string MemoryIntegrity = "kernel.memory-integrity";
    public const string CpuMitigations = "kernel.cpu-mitigations";

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

    // --- Seguridad ---
    public const string TamperProtection = "security.tamper-protection";
    public const string RealTimeProtection = "security.real-time-protection";
    public const string ControlledFolderAccess = "security.controlled-folder-access";

    // --- Arranque y sesión ---
    public const string FastStartup = "startup.fast-startup";
    public const string ScreenSaver = "startup.screen-saver";
    public const string InactivityLock = "startup.inactivity-lock";

    // --- Diagnóstico ---
    public const string UnexpectedShutdowns = "diagnostics.unexpected-shutdowns";
    public const string DevicesWithError = "diagnostics.devices-with-error";
    public const string TimerResolution = "diagnostics.timer-resolution";
    public const string VirtualizationFirmware = "diagnostics.virtualization-firmware";
}
