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

    /// <summary>
    /// Un REG_QWORD. Hace falta para LastKnownGoodTime, que es un FILETIME y no cabe en int.
    ///
    /// ReadInt lo truncaria a treinta y dos bits sin quejarse, y el resultado seria una
    /// fecha de 1601 en vez de un fallo de lectura: exactamente la clase de dato falso que
    /// pinta una fila de rojo por el motivo equivocado.
    /// </summary>
    public static long? ReadLong(RegistryHive hive, string keyPath, string valueName) =>
        ReadValue(hive, keyPath, valueName) switch
        {
            long l => l,
            int i => i,
            _ => null,
        };

    /// <summary>
    /// Un REG_MULTI_SZ como lista de lineas.
    ///
    /// Devuelve null solo si el valor no esta o no se deja leer. Una lista **vacia** es un
    /// resultado legitimo y distinto: en PagingFiles significa «sin archivo de paginacion»,
    /// que es una respuesta, no una falta de respuesta.
    /// </summary>
    public static IReadOnlyList<string>? ReadMultiString(
        RegistryHive hive, string keyPath, string valueName) =>
        ReadValue(hive, keyPath, valueName) as string[];

    /// <summary>
    /// true si el valor existe, sin importar su contenido. PendingFileRenameOperations es
    /// justo eso: lo que importa es que este, no lo que ponga.
    /// </summary>
    public static bool ReadValueExists(RegistryHive hive, string keyPath, string valueName) =>
        ReadValue(hive, keyPath, valueName) is not null;

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

    private static object? ReadValue(RegistryHive hive, string keyPath, string valueName) =>
        Probe(hive, keyPath, valueName).Raw;

    /// <summary>
    /// Lectura que además dice por qué no ha habido valor, cuando no lo ha habido.
    ///
    /// Los helpers de arriba devuelven null para los tres casos y eso basta cuando el
    /// ajuste no tiene comportamiento por defecto. Cuando lo tiene —y la mitad de las
    /// claves de esta capa solo existen si alguien ha tocado el interruptor— hace falta
    /// separar «no está» de «no me dejan»: en el primer caso Windows se comporta según su
    /// valor por defecto, que se conoce, y en el segundo no se sabe nada.
    /// </summary>
    public static RegistryValue Probe(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(keyPath);

            if (key is null)
            {
                return new RegistryValue(null, RegistryOutcome.KeyAbsent);
            }

            object? raw = key.GetValue(valueName);

            return raw is null
                ? new RegistryValue(null, RegistryOutcome.ValueAbsent)
                : new RegistryValue(raw, RegistryOutcome.Read);
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            return new RegistryValue(null, RegistryOutcome.Denied);
        }
    }
}

/// <summary>Resultado de intentar leer un valor del registro.</summary>
public enum RegistryOutcome
{
    /// <summary>El valor estaba ahí.</summary>
    Read,

    /// <summary>La clave existe pero el valor no. Casi siempre significa «nadie lo ha tocado».</summary>
    ValueAbsent,

    /// <summary>No existe ni la clave. Misma lectura que la anterior, pero se distinguen al depurar.</summary>
    KeyAbsent,

    /// <summary>La ACL no ha dejado abrirla. Esto sí es desconocido de verdad.</summary>
    Denied,
}

/// <summary>Lo leído del registro, con el motivo cuando no hay valor.</summary>
public readonly record struct RegistryValue(object? Raw, RegistryOutcome Outcome)
{
    /// <summary>true cuando el valor no está pero se ha podido mirar.</summary>
    public bool Absent => Outcome is RegistryOutcome.ValueAbsent or RegistryOutcome.KeyAbsent;

    public int? AsInt => Raw switch
    {
        int i => i,
        long l => unchecked((int)l),
        _ => null,
    };

    public bool? AsBool => AsInt switch
    {
        0 => false,
        null => null,
        _ => true,
    };

    public string? AsString => Raw as string;
}
