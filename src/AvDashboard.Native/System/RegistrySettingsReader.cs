using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using Microsoft.Win32;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>
/// Lectura de los ajustes de M1 que viven en el registro. Solo lectura: la escritura
/// llega en la Fase 2 y pasa por actuadores con snapshot previo.
///
/// Todas las lecturas son tolerantes a fallo y devuelven null cuando la clave no existe.
/// Un valor ausente y un valor desconocido no son lo mismo, y la UI necesita distinguirlos:
/// «no configurado» no se pinta igual que «mal configurado».
/// </summary>
[SupportedOSPlatform("windows")]
public static class RegistrySettingsReader
{
    private const string MmcssKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string VisualEffectsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string WindowsUpdateSettingsKey =
        @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string RebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string DefenderFeaturesKey = @"SOFTWARE\Microsoft\Windows Defender\Features";
    private const string DefenderPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender";

    public static SystemSettings Read()
    {
        Guid? activePlan = PowerConfiguration.GetActiveSchemeGuid();

        return new SystemSettings
        {
            ActivePowerPlanGuid = activePlan,
            ActivePowerPlanName = activePlan is Guid plan ? PowerConfiguration.GetSchemeName(plan) : null,
            UltimatePerformanceAvailable = PowerConfiguration.IsUltimatePerformanceAvailable(),

            FastStartupEnabled = ReadBool(RegistryHive.LocalMachine, PowerKey, "HiberbootEnabled"),

            MmcssSystemResponsiveness = ReadInt(RegistryHive.LocalMachine, MmcssKey, "SystemResponsiveness"),
            NetworkThrottlingIndex = ReadUInt(RegistryHive.LocalMachine, MmcssKey, "NetworkThrottlingIndex"),

            VisualEffects = ReadVisualEffects(),
            GameBarEnabled = ReadBool(RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled"),
            HardwareAcceleratedGpuScheduling =
                ReadInt(RegistryHive.LocalMachine, GraphicsDriversKey, "HwSchMode") switch
                {
                    2 => true,
                    1 => false,
                    _ => null,
                },

            WindowsUpdate = ReadWindowsUpdate(),
            Defender = ReadDefender(),
        };
    }

    private static VisualEffectsSetting ReadVisualEffects()
    {
        int? setting = ReadInt(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting");
        return setting switch
        {
            0 => VisualEffectsSetting.LetWindowsChoose,
            1 => VisualEffectsSetting.BestAppearance,
            2 => VisualEffectsSetting.BestPerformance,
            3 => VisualEffectsSetting.Custom,
            _ => VisualEffectsSetting.Unknown,
        };
    }

    private static WindowsUpdateStatus ReadWindowsUpdate()
    {
        DateTimeOffset? pausedUntil = null;
        string? raw = ReadString(RegistryHive.LocalMachine, WindowsUpdateSettingsKey,
            "PauseUpdatesExpiryTime");

        if (raw is not null && DateTimeOffset.TryParse(raw, out DateTimeOffset parsed))
        {
            pausedUntil = parsed;
        }

        int? activeStart = ReadInt(RegistryHive.LocalMachine, WindowsUpdateSettingsKey, "ActiveHoursStart");
        int? activeEnd = ReadInt(RegistryHive.LocalMachine, WindowsUpdateSettingsKey, "ActiveHoursEnd");

        return new WindowsUpdateStatus
        {
            PausedUntil = pausedUntil,
            PausedUntilKnown = raw is not null,
            ActiveHoursStart = activeStart is int start ? TimeSpan.FromHours(start) : null,
            ActiveHoursEnd = activeEnd is int end ? TimeSpan.FromHours(end) : null,
            RebootPending = KeyExists(RegistryHive.LocalMachine, RebootRequiredKey),
        };
    }

    private static DefenderStatus ReadDefender()
    {
        // TamperProtection: 5 = activo, 4 = inactivo. La clave esta protegida por ACL y
        // desde un proceso sin privilegios la lectura falla; eso es un null, no un false.
        int? tamper = ReadInt(RegistryHive.LocalMachine, DefenderFeaturesKey, "TamperProtection");
        int? disabledByPolicy = ReadInt(RegistryHive.LocalMachine, DefenderPolicyKey, "DisableAntiSpyware");

        return new DefenderStatus
        {
            TamperProtectionEnabled = tamper switch
            {
                5 => true,
                4 => false,
                _ => null,
            },
            RealTimeProtectionEnabled = disabledByPolicy switch
            {
                1 => false,
                0 => true,
                _ => null,
            },
        };
    }

    // --- Helpers de lectura tolerantes a fallo ---

    public static int? ReadInt(RegistryHive hive, string keyPath, string valueName)
    {
        object? value = ReadValue(hive, keyPath, valueName);
        return value switch
        {
            int i => i,
            long l => unchecked((int)l),
            _ => null,
        };
    }

    public static uint? ReadUInt(RegistryHive hive, string keyPath, string valueName)
    {
        object? value = ReadValue(hive, keyPath, valueName);
        return value switch
        {
            int i => unchecked((uint)i),
            long l => unchecked((uint)l),
            _ => null,
        };
    }

    public static bool? ReadBool(RegistryHive hive, string keyPath, string valueName) =>
        ReadInt(hive, keyPath, valueName) switch
        {
            0 => false,
            null => null,
            _ => true,
        };

    public static string? ReadString(RegistryHive hive, string keyPath, string valueName) =>
        ReadValue(hive, keyPath, valueName) as string;

    public static bool KeyExists(RegistryHive hive, string keyPath)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(keyPath);
            return key is not null;
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            return false;
        }
    }

    private static object? ReadValue(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName);
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            return null;
        }
    }
}
