using System.Runtime.Versioning;
using AvDashboard.Core.Settings;
using Microsoft.Win32;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>Lo que ha pasado al intentar escribir un ajuste.</summary>
public sealed record SettingWriteResult
{
    public required string Id { get; init; }
    public required bool Success { get; init; }
    public required string Message { get; init; }

    /// <summary>Valor que había antes. Es lo que hace posible deshacer.</summary>
    public string? PreviousValue { get; init; }

    public string? AppliedValue { get; init; }

    /// <summary>true si solo se ha calculado el cambio, sin aplicarlo.</summary>
    public bool DryRun { get; init; }
}

/// <summary>
/// Escritura de los ajustes del catálogo (M1, Fase 2).
///
/// El conmutador de más abajo <b>es</b> la lista blanca. No hay una ruta genérica que
/// escriba una clave arbitraria del registro: un ajuste que no esté aquí no se puede tocar
/// desde la API, por mucho que alguien construya la petición a mano. Esa es la diferencia
/// entre un actuador y una puerta trasera, y es el punto 7 del diseño.
///
/// Deliberadamente fuera, y no por falta de tiempo:
///
/// <list type="bullet">
/// <item>Integridad de memoria y mitigaciones de CPU: bajarlas reduce la seguridad de la
/// máquina de forma real, y el catálogo ya las marca como excluidas de todo automatismo.</item>
/// <item>Defender y el acceso controlado a carpetas: con la protección contra
/// manipulaciones activa no se pueden tocar por software, y fingir que sí sería mentir.</item>
/// <item>Lo que no es un ajuste sino una medida: apagados inesperados, reinicio pendiente,
/// dispositivos con error.</item>
/// <item>Lo que no tiene un valor único: offloads de NIC, indexación y tareas programadas
/// son por adaptador, por volumen y por tarea. Llegan cuando tengan su propia vista.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public static class SettingsWriter
{
    /// <summary>
    /// Ajustes que esta versión sabe escribir.
    ///
    /// La consola pregunta por esta lista en lugar de deducirlo del catálogo: así una fila
    /// nunca ofrece un interruptor que luego no hace nada.
    /// </summary>
    public static IReadOnlySet<string> SupportedIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Energía
        SettingIds.PowerPlan,
        SettingIds.ProcessorMin,
        SettingIds.ProcessorMax,
        SettingIds.CoreParking,
        SettingIds.UsbSelectiveSuspend,
        SettingIds.PcieAspm,
        SettingIds.DiskTimeout,
        SettingIds.SleepTimeout,
        SettingIds.DisplayTimeout,
        SettingIds.WakeTimers,

        // Kernel y rendimiento
        SettingIds.MmcssResponsiveness,
        SettingIds.NetworkThrottling,
        SettingIds.ProAudioPriority,
        SettingIds.PrioritySeparation,
        SettingIds.PowerThrottling,
        SettingIds.Transparency,
        SettingIds.GameMode,
        SettingIds.GameDvr,
        SettingIds.BackgroundApps,

        // Interrupciones y actualizaciones
        SettingIds.AutoRebootBlocked,
        SettingIds.DriversViaUpdate,
        SettingIds.DeliveryOptimization,
        SettingIds.Notifications,
        SettingIds.StoreAutoUpdate,
        SettingIds.StorageSense,
        SettingIds.CoInstallers,

        // Arranque y sesión
        SettingIds.FastStartup,
        SettingIds.ScreenSaver,
        SettingIds.InactivityLock,
        SettingIds.RestartApps,
    };

    /// <summary>
    /// Aplica un valor canónico —el mismo vocabulario que devuelve la lectura— a un ajuste.
    ///
    /// Con <paramref name="dryRun"/> se calcula el cambio y se devuelve sin tocar nada. No
    /// es un adorno: es lo que permite enseñar el diff antes de aplicar un perfil entero.
    /// </summary>
    public static SettingWriteResult Apply(string id, string value, bool dryRun = false)
    {
        Platform.EnsureWindows();

        if (!SupportedIds.Contains(id))
        {
            return Failed(id, "Este ajuste no se puede cambiar desde AV Dashboard.");
        }

        SettingDefinition? definition = SettingsCatalog.Find(id);

        if (definition is null)
        {
            return Failed(id, "Ajuste desconocido.");
        }

        // Doble cinturón: aunque el conmutador ya excluye los de riesgo de seguridad, se
        // vuelve a comprobar contra el catalogo. Si alguien anade un id a la lista por
        // descuido, esto lo para.
        if (definition.ExcludedFromProfiles)
        {
            return Failed(id, "Este ajuste queda fuera de los cambios automáticos por su " +
                              "impacto en la seguridad de la máquina. Hay que tocarlo a mano.");
        }

        try
        {
            return id switch
            {
                SettingIds.PowerPlan => ApplyPowerPlan(value, dryRun),

                SettingIds.ProcessorMin => Power(id, value, dryRun,
                    PowerConfiguration.Settings.ProcessorSubGroup,
                    PowerConfiguration.Settings.ProcessorMin),
                SettingIds.ProcessorMax => Power(id, value, dryRun,
                    PowerConfiguration.Settings.ProcessorSubGroup,
                    PowerConfiguration.Settings.ProcessorMax),

                // El aparcado se expresa al reves: 100 significa «no aparques ninguno».
                SettingIds.CoreParking => Power(id, value, dryRun,
                    PowerConfiguration.Settings.ProcessorSubGroup,
                    PowerConfiguration.Settings.CoreParkingMinCores,
                    Boolean(value) ? 0u : 100u),

                SettingIds.UsbSelectiveSuspend => Power(id, value, dryRun,
                    PowerConfiguration.Settings.UsbSubGroup,
                    PowerConfiguration.Settings.UsbSelectiveSuspend,
                    Boolean(value) ? 1u : 0u),
                SettingIds.PcieAspm => Power(id, value, dryRun,
                    PowerConfiguration.Settings.PcieSubGroup,
                    PowerConfiguration.Settings.PcieAspm,
                    Boolean(value) ? 2u : 0u),
                SettingIds.WakeTimers => Power(id, value, dryRun,
                    PowerConfiguration.Settings.SleepSubGroup,
                    PowerConfiguration.Settings.WakeTimers,
                    Boolean(value) ? 1u : 0u),

                SettingIds.DiskTimeout => Power(id, value, dryRun,
                    PowerConfiguration.Settings.DiskSubGroup,
                    PowerConfiguration.Settings.DiskTimeout),
                SettingIds.SleepTimeout => Power(id, value, dryRun,
                    PowerConfiguration.Settings.SleepSubGroup,
                    PowerConfiguration.Settings.StandbyTimeout),
                SettingIds.DisplayTimeout => Power(id, value, dryRun,
                    PowerConfiguration.Settings.VideoSubGroup,
                    PowerConfiguration.Settings.DisplayTimeout),

                SettingIds.MmcssResponsiveness => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, MmcssKey, "SystemResponsiveness"),
                SettingIds.NetworkThrottling => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, MmcssKey, "NetworkThrottlingIndex"),
                SettingIds.ProAudioPriority => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, ProAudioKey, "Priority"),
                SettingIds.PrioritySeparation => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, PriorityControlKey, "Win32PrioritySeparation"),

                // Guardado invertido: la clave se llama «throttling apagado».
                SettingIds.PowerThrottling => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, PowerThrottlingKey, "PowerThrottlingOff",
                    inverted: true),

                SettingIds.Transparency => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency"),
                SettingIds.GameMode => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled"),
                SettingIds.GameDvr => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled"),

                // Tambien invertido: «desactivadas globalmente».
                SettingIds.BackgroundApps => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, BackgroundAppsKey, "GlobalUserDisabled",
                    inverted: true),

                SettingIds.AutoRebootBlocked => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, WindowsUpdateAuPolicyKey,
                    "NoAutoRebootWithLoggedOnUsers"),

                // La directiva dice «excluir drivers»; la fila dice «drivers via Update».
                SettingIds.DriversViaUpdate => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, WindowsUpdatePolicyKey,
                    "ExcludeWUDriversInQualityUpdate", inverted: true),

                SettingIds.DeliveryOptimization => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, DeliveryOptimizationPolicyKey, "DODownloadMode"),
                SettingIds.Notifications => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, PushNotificationsKey, "ToastEnabled"),
                SettingIds.StoreAutoUpdate => Dword(id, value, dryRun,
                    RegistryHive.CurrentUser, StoreUpdateKey, "AutoDownload"),
                SettingIds.StorageSense => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, StorageSenseKey, "AllowStorageSenseGlobal"),
                SettingIds.CoInstallers => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, DeviceInstallerKey, "DisableCoInstallers",
                    inverted: true),

                SettingIds.FastStartup => Flag(id, value, dryRun,
                    RegistryHive.LocalMachine, PowerKey, "HiberbootEnabled"),
                SettingIds.InactivityLock => Dword(id, value, dryRun,
                    RegistryHive.LocalMachine, PoliciesSystemKey, "InactivityTimeoutSecs"),
                SettingIds.RestartApps => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, WinlogonKey, "RestartApps"),

                // ScreenSaveActive se guarda como cadena «1» o «0», no como entero.
                SettingIds.ScreenSaver => Text(id, value, dryRun,
                    RegistryHive.CurrentUser, DesktopKey, "ScreenSaveActive",
                    Boolean(value) ? "1" : "0"),

                _ => Failed(id, "Este ajuste no se puede cambiar desde AV Dashboard."),
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                      or global::System.Security.SecurityException)
        {
            return Failed(id,
                "Sin permisos para escribir. Este ajuste es de máquina y necesita que el " +
                "agente corra como servicio.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or FormatException)
        {
            return Failed(id, ex.Message);
        }
    }

    private static bool Boolean(string value) =>
        string.Equals(value, SettingReading.BooleanOn, StringComparison.OrdinalIgnoreCase);

    // --------------------------------------------------------------------- Energía

    private static SettingWriteResult ApplyPowerPlan(string value, bool dryRun)
    {
        Guid? current = PowerConfiguration.GetActiveSchemeGuid();
        string previous = current is Guid c ? PowerConfiguration.GetSchemeName(c) ?? c.ToString() : "desconocido";

        Guid target = value.ToLowerInvariant() switch
        {
            "ultimate" => PowerConfiguration.UltimatePerformanceScheme,
            "high" => PowerConfiguration.HighPerformanceScheme,
            "balanced" => PowerConfiguration.BalancedScheme,
            _ => Guid.Empty,
        };

        if (target == Guid.Empty)
        {
            return Failed(SettingIds.PowerPlan, $"«{value}» no es un plan conocido.");
        }

        // Ultimate Performance no viene de serie en todas las ediciones. Si no esta, se
        // duplica antes en vez de fallar con un error de Windows que no dice nada.
        if (target == PowerConfiguration.UltimatePerformanceScheme &&
            !PowerConfiguration.IsUltimatePerformanceAvailable() &&
            !dryRun &&
            !PowerConfiguration.TryDuplicateUltimatePerformance())
        {
            return Failed(SettingIds.PowerPlan,
                "Ultimate Performance no está disponible y no se ha podido duplicar. " +
                "Prueba con Alto rendimiento.");
        }

        if (dryRun)
        {
            return Ok(SettingIds.PowerPlan, previous, value, dryRun: true);
        }

        return PowerConfiguration.SetActiveScheme(target)
            ? Ok(SettingIds.PowerPlan, previous, value)
            : Failed(SettingIds.PowerPlan, "Windows ha rechazado el cambio de plan.");
    }

    private static SettingWriteResult Power(
        string id, string value, bool dryRun, Guid subGroup, Guid setting, uint? explicitValue = null)
    {
        uint? before = PowerConfiguration.ReadActiveAcValue(subGroup, setting);

        uint target = explicitValue ?? Parse(value);

        if (dryRun)
        {
            return Ok(id, before?.ToString(), target.ToString(), dryRun: true);
        }

        return PowerConfiguration.WriteActiveAcValue(subGroup, setting, target)
            ? Ok(id, before?.ToString(), target.ToString())
            : Failed(id, "El plan de energía activo ha rechazado el cambio.");
    }

    // -------------------------------------------------------------------- Registro

    private static SettingWriteResult Dword(
        string id, string value, bool dryRun, RegistryHive hive, string key, string name)
    {
        uint target = Parse(value);
        string? before = RegistrySettingsReader.ReadUInt(hive, key, name)?.ToString();

        if (dryRun)
        {
            return Ok(id, before, target.ToString(), dryRun: true);
        }

        WriteDword(hive, key, name, target);
        return Ok(id, before, target.ToString());
    }

    /// <summary>Un booleano del catálogo, con o sin inversión respecto a la clave real.</summary>
    private static SettingWriteResult Flag(
        string id, string value, bool dryRun, RegistryHive hive, string key, string name,
        bool inverted = false)
    {
        bool wanted = Boolean(value);
        uint target = (inverted ? !wanted : wanted) ? 1u : 0u;

        bool? rawBefore = RegistrySettingsReader.ReadBool(hive, key, name);
        string? before = rawBefore is null
            ? null
            : (inverted ? !rawBefore.Value : rawBefore.Value)
                ? SettingReading.BooleanOn
                : SettingReading.BooleanOff;

        if (dryRun)
        {
            return Ok(id, before, value, dryRun: true);
        }

        WriteDword(hive, key, name, target);
        return Ok(id, before, value);
    }

    private static SettingWriteResult Text(
        string id, string value, bool dryRun, RegistryHive hive, string key, string name,
        string raw)
    {
        string? before = RegistrySettingsReader.ReadString(hive, key, name);

        if (dryRun)
        {
            return Ok(id, before, value, dryRun: true);
        }

        using RegistryKey subKey = Open(hive, key);
        subKey.SetValue(name, raw, RegistryValueKind.String);

        return Ok(id, before, value);
    }

    private static void WriteDword(RegistryHive hive, string key, string name, uint value)
    {
        using RegistryKey subKey = Open(hive, key);

        // El registro no tiene DWORD sin signo: 0xFFFFFFFF se escribe como -1 y Windows lo
        // lee igual. Sin este unchecked, NetworkThrottlingIndex desborda.
        subKey.SetValue(name, unchecked((int)value), RegistryValueKind.DWord);
    }

    private static RegistryKey Open(RegistryHive hive, string key)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        // CreateSubKey y no OpenSubKey: media docena de estas claves de directiva no
        // existen hasta que alguien las escribe por primera vez.
        return baseKey.CreateSubKey(key, writable: true)
               ?? throw new IOException($"No se ha podido abrir {key}.");
    }

    private static uint Parse(string value) =>
        uint.TryParse(value, out uint parsed)
            ? parsed
            : throw new FormatException($"«{value}» no es un número válido para este ajuste.");

    private static SettingWriteResult Ok(
        string id, string? previous, string? applied, bool dryRun = false) => new()
    {
        Id = id,
        Success = true,
        Message = dryRun ? "Sin aplicar: solo se ha calculado el cambio." : "Aplicado.",
        PreviousValue = previous,
        AppliedValue = applied,
        DryRun = dryRun,
    };

    private static SettingWriteResult Failed(string id, string message) => new()
    {
        Id = id,
        Success = false,
        Message = message,
    };

    // ------------------------------------------------------------------------ Claves

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string MmcssKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ProAudioKey = MmcssKey + @"\Tasks\Pro Audio";
    private const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string BackgroundAppsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string WindowsUpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string WindowsUpdateAuPolicyKey = WindowsUpdatePolicyKey + @"\AU";
    private const string DeliveryOptimizationPolicyKey =
        @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string PushNotificationsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string StoreUpdateKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsStore\WindowsUpdate";
    private const string StorageSenseKey = @"SOFTWARE\Policies\Microsoft\Windows\StorageSense";
    private const string DeviceInstallerKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer";
    private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string PoliciesSystemKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
}
