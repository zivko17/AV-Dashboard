using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ComponentModel;
using System.Diagnostics;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Native.Network;
using AvDashboard.Core.Networking;
using AvDashboard.Core.Settings;
using Microsoft.Win32;
using EventLogQuery = global::System.Diagnostics.Eventing.Reader.EventLogQuery;
using EventLogReader = global::System.Diagnostics.Eventing.Reader.EventLogReader;
using EventLogException = global::System.Diagnostics.Eventing.Reader.EventLogException;
using PathType = global::System.Diagnostics.Eventing.Reader.PathType;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>
/// Produce las lecturas que el catálogo de <see cref="SettingsCatalog"/> compara contra sus
/// objetivos.
///
/// Aquí no hay ningún criterio sobre qué está bien o mal: esta clase solo sabe leer y dar
/// formato. El juicio vive entero en Core, que es donde se puede probar sin Windows delante.
/// Esa separación es deliberada, y es lo que hace que añadir un ajuste sea una fila en el
/// catálogo más una línea aquí.
///
/// Todas las lecturas son tolerantes a fallo. Un valor que no se ha podido leer sale como
/// ausente y el catálogo lo trata como desconocido, no como incorrecto: sin privilegios hay
/// media docena de claves que no se dejan, y pintarlas en rojo llenaría la pantalla de
/// falsos positivos hasta que dejaras de mirarlos.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SettingsProbe
{
    public static IReadOnlyList<SettingReading> Read()
    {
        Platform.EnsureWindows();

        List<SettingReading> readings = new();

        ReadPower(readings);
        ReadKernel(readings);
        ReadUpdates(readings);
        ReadSecurity(readings);
        ReadStartup(readings);
        ReadNetwork(readings);
        ReadDiagnostics(readings);

        return readings;
    }

    // ------------------------------------------------------------------------ Energía

    private static void ReadPower(List<SettingReading> readings)
    {
        Guid? active = PowerConfiguration.GetActiveSchemeGuid();

        readings.Add(SettingReading.Of(
            SettingIds.PowerPlan,
            DescribePlan(active),
            active is Guid plan ? PowerConfiguration.GetSchemeName(plan) : null));

        readings.Add(SettingReading.Of(
            SettingIds.UltimateAvailable,
            PowerConfiguration.IsUltimatePerformanceAvailable()));

        AddAc(readings, SettingIds.ProcessorMin,
            PowerConfiguration.Settings.ProcessorSubGroup,
            PowerConfiguration.Settings.ProcessorMin,
            v => (v.ToString(), $"{v} %"));

        AddAc(readings, SettingIds.ProcessorMax,
            PowerConfiguration.Settings.ProcessorSubGroup,
            PowerConfiguration.Settings.ProcessorMax,
            v => (v.ToString(), $"{v} %"));

        // CPMINCORES es el porcentaje minimo de nucleos sin aparcar: 100 significa que no se
        // aparca ninguno, o sea que el aparcado esta efectivamente desactivado.
        AddAc(readings, SettingIds.CoreParking,
            PowerConfiguration.Settings.ProcessorSubGroup,
            PowerConfiguration.Settings.CoreParkingMinCores,
            v => (v >= 100 ? SettingReading.BooleanOff : SettingReading.BooleanOn,
                  v >= 100 ? "todos despiertos" : $"aparca por debajo del {v} %"));

        AddAc(readings, SettingIds.UsbSelectiveSuspend,
            PowerConfiguration.Settings.UsbSubGroup,
            PowerConfiguration.Settings.UsbSelectiveSuspend,
            v => (v == 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn, null));

        AddAc(readings, SettingIds.PcieAspm,
            PowerConfiguration.Settings.PcieSubGroup,
            PowerConfiguration.Settings.PcieAspm,
            v => (v == 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn,
                  v switch { 0 => "desactivado", 1 => "moderado", 2 => "máximo", _ => null }));

        AddAc(readings, SettingIds.DiskTimeout,
            PowerConfiguration.Settings.DiskSubGroup,
            PowerConfiguration.Settings.DiskTimeout,
            v => (v.ToString(), DescribeTimeout(v)));

        AddAc(readings, SettingIds.SleepTimeout,
            PowerConfiguration.Settings.SleepSubGroup,
            PowerConfiguration.Settings.StandbyTimeout,
            v => (v.ToString(), DescribeTimeout(v)));

        AddAc(readings, SettingIds.DisplayTimeout,
            PowerConfiguration.Settings.VideoSubGroup,
            PowerConfiguration.Settings.DisplayTimeout,
            v => (v.ToString(), DescribeTimeout(v)));

        AddAc(readings, SettingIds.WakeTimers,
            PowerConfiguration.Settings.SleepSubGroup,
            PowerConfiguration.Settings.WakeTimers,
            v => (v == 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn,
                  v switch { 0 => "desactivados", 1 => "activados", 2 => "solo importantes", _ => null }));

        // HibernateEnabled cuelga de Control\Power, no de Control\Session Manager\Power, que
        // es donde vive HiberbootEnabled. Estaban leyendose de la misma constante y por eso
        // la hibernacion salia siempre sin lectura mientras el inicio rapido salia bien.
        RegistryValue hibernate = RegistrySettingsReader.Probe(
            RegistryHive.LocalMachine, HibernationKey, "HibernateEnabled");

        readings.Add(hibernate.Outcome switch
        {
            RegistryOutcome.Read => SettingReading.Of(SettingIds.Hibernation, hibernate.AsBool),
            _ => SettingReading.Missing(SettingIds.Hibernation,
                hibernate.Outcome == RegistryOutcome.Denied
                    ? "sin permiso para leer la clave de energia"
                    : "la clave de energia no declara HibernateEnabled"),
        });

        readings.Add(ReadWakeArmedTasks());
    }

    private static string? DescribePlan(Guid? scheme)
    {
        if (scheme is not Guid plan)
        {
            return null;
        }

        if (plan == PowerConfiguration.UltimatePerformanceScheme)
        {
            return "ultimate";
        }

        if (plan == PowerConfiguration.HighPerformanceScheme)
        {
            return "high";
        }

        return plan == PowerConfiguration.BalancedScheme ? "balanced" : "other";
    }

    private static string DescribeTimeout(uint seconds) =>
        seconds == 0 ? "nunca" : $"{TimeSpan.FromSeconds(seconds).TotalMinutes:0.#} min";

    /// <summary>
    /// Lee un ajuste del plan de energia activo y lo anade ya formateado.
    ///
    /// Que el plan no exponga un ajuste es normal —depende del hardware y de la edicion de
    /// Windows—, asi que sale como ausente y el catalogo lo trata como desconocido en vez
    /// de como incorrecto.
    /// </summary>
    private static void AddAc(
        List<SettingReading> readings, string id, Guid subGroup, Guid setting,
        Func<uint, (string Value, string? Display)> format)
    {
        uint? raw = PowerConfiguration.ReadActiveAcValue(subGroup, setting);

        if (raw is not uint value)
        {
            readings.Add(SettingReading.Missing(id, "el plan activo no expone este ajuste"));
            return;
        }

        (string canonical, string? display) = format(value);
        readings.Add(SettingReading.Of(id, canonical, display));
    }

    /// <summary>
    /// Anade una lectura cuyo valor ausente en el registro significa algo, y no «no se sabe».
    ///
    /// La mitad de estas claves solo existen cuando alguien ha tocado el ajuste: mientras
    /// nadie lo toque, Windows se comporta segun su valor por defecto, y ese comportamiento
    /// esta documentado. Tratar esa ausencia como desconocido dejaba en gris seis filas de
    /// una maquina recien instalada -justo las que mas falta hace ver- con la excusa de no
    /// inventarse nada, cuando el dato se sabia.
    ///
    /// Un fallo de permisos si es desconocido de verdad, y por eso <see cref="RegistryValue"/>
    /// los distingue. Solo se rellena por defecto lo que esta documentado: el ajuste cuyo
    /// valor de fabrica no es estable entre versiones se queda sin lectura, pero diciendo
    /// por que.
    /// </summary>
    private static void AddOrDefault(
        List<SettingReading> readings, string id, RegistryValue probe,
        Func<int, (string Value, string? Display)> format,
        (string Value, string? Display) whenAbsent)
    {
        if (probe.Outcome == RegistryOutcome.Denied)
        {
            readings.Add(SettingReading.Missing(id, "sin permiso para leer la clave"));
            return;
        }

        if (probe.AsInt is not int value)
        {
            readings.Add(SettingReading.Of(id, whenAbsent.Value, whenAbsent.Display));
            return;
        }

        (string canonical, string? display) = format(value);
        readings.Add(SettingReading.Of(id, canonical, display));
    }

    /// <summary>
    /// Tareas programadas con «despertar el equipo» armado.
    ///
    /// El ajuste global de temporizadores de reactivacion dice si se permite; esto dice
    /// cuales lo tienen puesto, que es lo accionable. El desfragmentador y los
    /// actualizadores de fabricante son los sospechosos habituales.
    ///
    /// Se usa el COM del planificador por ProgID en vez de una libreria: es la via
    /// documentada y no anade dependencia. El recorrido es recursivo con tope, porque en
    /// una maquina con software de fabricante hay carpetas anidadas de sobra.
    /// </summary>
    private static SettingReading ReadWakeArmedTasks()
    {
        const int limit = 4000;

        try
        {
            Type? type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                return SettingReading.Missing(SettingIds.WakeArmedTasks,
                    "el planificador de tareas no esta disponible");
            }

            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();

            List<string> armed = new();
            Budget budget = new() { Remaining = limit };

            // La raiz se saca a una variable de tipo object a proposito: si se pasara la
            // expresion dynamic directamente, la llamada a Walk se resolveria en tiempo de
            // ejecucion, y el enlazador dinamico no lleva bien los parametros por
            // referencia. Con object, Walk es una llamada estatica normal.
            object root = service.GetFolder("\\");

            Walk(root, armed, budget);

            return SettingReading.Of(SettingIds.WakeArmedTasks, armed.Count,
                armed.Count == 0
                    ? "ninguna"
                    : string.Join(", ", armed.Take(4)) + (armed.Count > 4 ? "…" : string.Empty));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException
                                      or global::System.Reflection.TargetInvocationException
                                      or MissingMemberException)
        {
            return SettingReading.Missing(SettingIds.WakeArmedTasks,
                "no se ha podido consultar el planificador de tareas");
        }
    }

    /// <summary>Tope de tareas a visitar, para que una jerarquia rara no cuelgue el ciclo.</summary>
    private sealed class Budget
    {
        public int Remaining { get; set; }
    }

    private static void Walk(object folder, List<string> armed, Budget budget)
    {
        dynamic current = folder;

        foreach (dynamic task in current.GetTasks(0))
        {
            if (--budget.Remaining <= 0)
            {
                return;
            }

            // Una tarea deshabilitada no despierta nada, asi que no cuenta.
            if ((bool)task.Enabled && (bool)task.Definition.Settings.WakeToRun)
            {
                armed.Add((string)task.Name);
            }
        }

        foreach (dynamic child in current.GetFolders(0))
        {
            if (budget.Remaining <= 0)
            {
                return;
            }

            Walk((object)child, armed, budget);
        }
    }

    // ------------------------------------------------------------- Kernel y rendimiento

    private static void ReadKernel(List<SettingReading> readings)
    {
        readings.Add(SettingReading.Of(
            SettingIds.MmcssResponsiveness,
            RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, MmcssKey, "SystemResponsiveness")));

        uint? throttlingIndex =
            RegistrySettingsReader.ReadUInt(RegistryHive.LocalMachine, MmcssKey, "NetworkThrottlingIndex");

        readings.Add(SettingReading.OfUnsigned(
            SettingIds.NetworkThrottling,
            throttlingIndex,
            // Sin limite se escribe 0xFFFFFFFF, que como decimal son diez digitos que no dicen
            // nada. En la vista se lee mejor la intencion que el numero.
            throttlingIndex == uint.MaxValue ? "sin límite" : null));

        readings.Add(SettingReading.Of(
            SettingIds.ProAudioPriority,
            RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, ProAudioKey, "Priority")));

        readings.Add(SettingReading.Of(
            SettingIds.PrioritySeparation,
            RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, PriorityControlKey,
                "Win32PrioritySeparation")));

        // PowerThrottlingOff = 1 significa que el estrangulamiento esta apagado.
        bool? throttlingOff = RegistrySettingsReader.ReadBool(
            RegistryHive.LocalMachine, PowerThrottlingKey, "PowerThrottlingOff");

        readings.Add(SettingReading.Of(
            SettingIds.PowerThrottling,
            throttlingOff is null ? null : !throttlingOff));

        readings.Add(SettingReading.Of(SettingIds.VisualEffects, DescribeVisualEffects()));

        readings.Add(SettingReading.Of(
            SettingIds.Transparency,
            RegistrySettingsReader.ReadBool(RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency")));

        readings.Add(SettingReading.Of(
            SettingIds.Hags,
            RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, GraphicsDriversKey, "HwSchMode") switch
            {
                2 => SettingReading.BooleanOn,
                1 => SettingReading.BooleanOff,
                _ => null,
            }));

        readings.Add(SettingReading.Of(
            SettingIds.GameMode,
            RegistrySettingsReader.ReadBool(RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled")));

        // Dos claves: la de usuario y la de directiva. Basta con que una lo apague.
        bool? userDvr = RegistrySettingsReader.ReadBool(
            RegistryHive.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled");
        bool? policyDvr = RegistrySettingsReader.ReadBool(
            RegistryHive.LocalMachine, GameDvrPolicyKey, "AllowGameDVR");

        // La regla —una directiva que dice «no» gana siempre— vive en Core porque el
        // actuador la necesita tambien: escribiendo la preferencia sin mirar la directiva,
        // anunciaba en el Timeline un cambio que la maquina no hacia.
        readings.Add(SettingReading.Of(
            SettingIds.GameDvr,
            PolicyOverride.Effective(userDvr, policyDvr),
            (userDvr, policyDvr) switch
            {
                (_, false) => "desactivado por directiva",
                (false, _) => (string?)null,
                (null, null) => null,
                _ => null,
            }));

        // GlobalUserDisabled = 1 significa que estan desactivadas, que es lo que queremos.
        AddOrDefault(readings, SettingIds.BackgroundApps,
            RegistrySettingsReader.Probe(RegistryHive.CurrentUser, BackgroundAppsKey,
                "GlobalUserDisabled"),
            v => (v != 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn, null),
            // El valor solo se escribe cuando alguien mueve el interruptor. Mientras nadie
            // lo toque, las aplicaciones de la Store pueden correr en segundo plano.
            (SettingReading.BooleanOn, "permitidas (valor por defecto)"));

        readings.Add(SettingReading.Of(
            SettingIds.MemoryIntegrity,
            RegistrySettingsReader.ReadBool(RegistryHive.LocalMachine, HvciKey, "Enabled")));

        int? mitigations = RegistrySettingsReader.ReadInt(
            RegistryHive.LocalMachine, MemoryManagementKey, "FeatureSettingsOverride");

        readings.Add(SettingReading.Of(
            SettingIds.CpuMitigations,
            mitigations switch
            {
                null => "default",
                3 => "disabled",
                0 => "enabled",
                int other => other.ToString(),
            },
            mitigations switch
            {
                null => "por defecto",
                3 => "mitigaciones desactivadas",
                0 => "mitigaciones activas",
                _ => null,
            }));
    }

    private static string? DescribeVisualEffects() =>
        RegistrySettingsReader.ReadInt(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting") switch
        {
            0 => "LetWindowsChoose",
            1 => "BestAppearance",
            2 => "BestPerformance",
            3 => "Custom",
            _ => null,
        };

    // --------------------------------------------- Interrupciones y actualizaciones

    private static void ReadUpdates(List<SettingReading> readings)
    {
        RegistryValue pause = RegistrySettingsReader.Probe(
            RegistryHive.LocalMachine, WindowsUpdateSettingsKey, "PauseUpdatesExpiryTime");

        if (pause.Outcome == RegistryOutcome.Denied)
        {
            readings.Add(SettingReading.Missing(SettingIds.UpdatePaused,
                "sin permiso para leer los ajustes de Windows Update"));
        }
        else if (pause.AsString is string pausedRaw &&
                 DateTimeOffset.TryParse(pausedRaw, out DateTimeOffset until))
        {
            bool active = until > DateTimeOffset.UtcNow;
            readings.Add(SettingReading.Of(SettingIds.UpdatePaused, active,
                active ? $"hasta {until.LocalDateTime:d MMM}" : "caducada"));
        }
        else
        {
            // Windows borra el valor al levantar la pausa, asi que ausente es «no pausado».
            // Una fecha que no se parsea tambien: lo que no se puede afirmar es que siga en
            // pausa, y esa es la lectura que importa.
            readings.Add(SettingReading.Of(SettingIds.UpdatePaused, false,
                pause.Absent ? "sin pausa" : null));
        }

        readings.Add(SettingReading.Of(
            SettingIds.RebootPending, RebootPending(), RebootPendingDetail()));

        readings.Add(SettingReading.Of(
            SettingIds.AutoRebootBlocked,
            RegistrySettingsReader.ReadBool(RegistryHive.LocalMachine, WindowsUpdateAuPolicyKey,
                "NoAutoRebootWithLoggedOnUsers")));

        // La directiva se llama «excluir drivers»: activa significa que Windows NO los toca,
        // que es lo que queremos. El catalogo pide «off» para la fila «drivers via Update».
        AddOrDefault(readings, SettingIds.DriversViaUpdate,
            RegistrySettingsReader.Probe(RegistryHive.LocalMachine, WindowsUpdatePolicyKey,
                "ExcludeWUDriversInQualityUpdate"),
            v => (v != 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn, null),
            // Directiva no configurada es el caso normal, y significa que Windows si los
            // instala con las actualizaciones de calidad.
            (SettingReading.BooleanOn, "sin directiva: Windows los instala"));

        // La directiva manda sobre la configuracion; si no hay directiva se mira la otra.
        int? downloadMode =
            RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, DeliveryOptimizationPolicyKey,
                "DODownloadMode")
            ?? RegistrySettingsReader.ReadInt(RegistryHive.LocalMachine, DeliveryOptimizationConfigKey,
                "DODownloadMode");

        readings.Add(SettingReading.Of(
            SettingIds.DeliveryOptimization,
            downloadMode,
            downloadMode switch
            {
                0 => "solo HTTP",
                1 => "P2P en la red local",
                2 => "P2P en el dominio",
                3 => "P2P en internet",
                99 => "sin descarga entre pares",
                _ => null,
            }));

        readings.Add(SettingReading.Of(SettingIds.FocusAssist, DescribeFocusAssist()));

        readings.Add(SettingReading.Of(
            SettingIds.Notifications,
            RegistrySettingsReader.ReadBool(RegistryHive.CurrentUser, PushNotificationsKey, "ToastEnabled")));

        AddOrDefault(readings, SettingIds.StoreAutoUpdate,
            RegistrySettingsReader.Probe(RegistryHive.CurrentUser, StoreUpdateKey, "AutoDownload"),
            v => (v.ToString(), v switch
            {
                2 => "desactivadas",
                4 => "automaticas",
                _ => null,
            }),
            // La clave no existe hasta que se toca el interruptor de la Store, y hasta
            // entonces la Store se actualiza sola: eso es un 4, no un desconocido.
            ("4", "automaticas (valor por defecto)"));

        // DisableCoInstallers = 1 significa que estan desactivados.
        bool? coInstallersDisabled = RegistrySettingsReader.ReadBool(
            RegistryHive.LocalMachine, DeviceInstallerKey, "DisableCoInstallers");

        readings.Add(SettingReading.Of(
            SettingIds.CoInstallers,
            coInstallersDisabled is null ? null : !coInstallersDisabled));

        readings.Add(ReadSearchIndexing());

        readings.Add(SettingReading.Of(
            SettingIds.StorageSense,
            RegistrySettingsReader.ReadBool(RegistryHive.LocalMachine, StorageSenseKey,
                "AllowStorageSenseGlobal")));
    }

    private static bool RebootPending() =>
        RegistrySettingsReader.KeyExists(RegistryHive.LocalMachine, RebootRequiredKey)
        || RegistrySettingsReader.KeyExists(RegistryHive.LocalMachine, CbsRebootPendingKey)
        || RegistrySettingsReader.ReadValueExists(RegistryHive.LocalMachine, SessionManagerKey,
            "PendingFileRenameOperations");

    private static string? RebootPendingDetail()
    {
        List<string> sources = new();

        if (RegistrySettingsReader.KeyExists(RegistryHive.LocalMachine, RebootRequiredKey))
        {
            sources.Add("Windows Update");
        }

        if (RegistrySettingsReader.KeyExists(RegistryHive.LocalMachine, CbsRebootPendingKey))
        {
            sources.Add("servicio de componentes");
        }

        if (RegistrySettingsReader.ReadValueExists(RegistryHive.LocalMachine, SessionManagerKey,
                "PendingFileRenameOperations"))
        {
            sources.Add("ficheros pendientes de renombrar");
        }

        return sources.Count == 0 ? null : string.Join(", ", sources);
    }

    private static string? DescribeFocusAssist()
    {
        // El estado vive bajo una clave de CloudStore con un sufijo que cambia entre
        // compilaciones, asi que se busca la subclave por prefijo en lugar de fijarla.
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(QuietHoursRoot);
            if (root is null)
            {
                return null;
            }

            string? profile = root.GetSubKeyNames()
                .FirstOrDefault(n => n.Contains("quiethoursprofile", StringComparison.OrdinalIgnoreCase));

            return profile is null
                ? null
                : profile.Contains("alarmsonly", StringComparison.OrdinalIgnoreCase) ? "AlarmsOnly"
                : profile.Contains("priorityonly", StringComparison.OrdinalIgnoreCase) ? "PriorityOnly"
                : "Off";
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------- Seguridad

    private static void ReadSecurity(List<SettingReading> readings)
    {
        int? tamper = RegistrySettingsReader.ReadInt(
            RegistryHive.LocalMachine, DefenderFeaturesKey, "TamperProtection");

        readings.Add(tamper switch
        {
            5 => SettingReading.Of(SettingIds.TamperProtection, true, "activa"),
            4 => SettingReading.Of(SettingIds.TamperProtection, false, "inactiva"),
            _ => SettingReading.Missing(SettingIds.TamperProtection,
                "clave protegida: hace falta ejecutar el agente como servicio"),
        });

        readings.Add(ReadRealTimeProtection());

        AddOrDefault(readings, SettingIds.ControlledFolderAccess,
            RegistrySettingsReader.Probe(RegistryHive.LocalMachine, ControlledFolderAccessKey,
                "EnableControlledFolderAccess"),
            v => (v == 0 ? SettingReading.BooleanOff : SettingReading.BooleanOn, v switch
            {
                1 => "activado",
                2 => "solo auditoria",
                _ => null,
            }),
            // Defender crea la clave vacia; el valor aparece la primera vez que se activa.
            (SettingReading.BooleanOff, "desactivado (valor por defecto)"));
    }

    /// <summary>
    /// Estado real de la proteccion en tiempo real.
    ///
    /// Antes se deducia de DisableAntiSpyware, que es una directiva: sin directiva -el caso
    /// normal- la fila salia vacia, y ademas la directiva no dice si alguien ha apagado la
    /// proteccion desde la propia interfaz de Defender. MSFT_MpComputerStatus si lo dice, y
    /// se lee sin privilegios. La directiva se conserva porque manda sobre el estado.
    /// </summary>
    private static SettingReading ReadRealTimeProtection()
    {
        int? disabledByPolicy = RegistrySettingsReader.ReadInt(
            RegistryHive.LocalMachine, DefenderPolicyKey, "DisableAntiSpyware");

        if (disabledByPolicy == 1)
        {
            return SettingReading.Of(SettingIds.RealTimeProtection, false, "desactivada por directiva");
        }

        try
        {
            ManagementScope scope = new(@"root\Microsoft\Windows\Defender");
            ObjectQuery query = new("SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");

            using ManagementObjectSearcher searcher = new(scope, query);

            foreach (ManagementBaseObject status in searcher.Get())
            {
                using (status)
                {
                    if (status["RealTimeProtectionEnabled"] is bool enabled)
                    {
                        return SettingReading.Of(SettingIds.RealTimeProtection, enabled);
                    }
                }
            }

            return SettingReading.Missing(SettingIds.RealTimeProtection,
                "Defender no publica su estado: puede haber otro antivirus al mando");
        }
        catch (ManagementException)
        {
            return SettingReading.Missing(SettingIds.RealTimeProtection,
                "el espacio de nombres de Defender no ha respondido");
        }
    }

    // ---------------------------------------------------------------- Arranque y sesión

    private static void ReadStartup(List<SettingReading> readings)
    {
        readings.Add(SettingReading.Of(
            SettingIds.FastStartup,
            RegistrySettingsReader.ReadBool(RegistryHive.LocalMachine, PowerKey, "HiberbootEnabled")));

        // ScreenSaveActive se guarda como cadena «1» o «0», no como entero.
        string? screenSaver = RegistrySettingsReader.ReadString(
            RegistryHive.CurrentUser, DesktopKey, "ScreenSaveActive");

        readings.Add(SettingReading.Of(
            SettingIds.ScreenSaver,
            screenSaver switch { "1" => true, "0" => false, _ => (bool?)null }));

        // RestartApps es el unico de este grupo que no se completa con su valor por defecto:
        // el interruptor de «relanzar aplicaciones» no escribe nada hasta que se toca y el
        // valor de fabrica ha cambiado entre versiones de Windows 11. Suponerlo apagado
        // pintaria la fila en verde sin haberla leido, que es peor que dejarla en gris.
        RegistryValue restartApps = RegistrySettingsReader.Probe(
            RegistryHive.CurrentUser, WinlogonKey, "RestartApps");

        readings.Add(restartApps.AsBool is bool restart
            ? SettingReading.Of(SettingIds.RestartApps, restart)
            : SettingReading.Missing(SettingIds.RestartApps,
                restartApps.Outcome == RegistryOutcome.Denied
                    ? "sin permiso para leer la clave de Winlogon"
                    : "el interruptor no se ha tocado nunca y Windows no escribe el valor hasta entonces"));

        AddOrDefault(readings, SettingIds.InactivityLock,
            RegistrySettingsReader.Probe(RegistryHive.LocalMachine, PoliciesSystemKey,
                "InactivityTimeoutSecs"),
            v => (v.ToString(), v == 0 ? "nunca" : $"{v} s"),
            // La directiva «limite de inactividad del equipo» sin configurar equivale a 0:
            // Windows no bloquea la sesion por su cuenta.
            ("0", "nunca (sin directiva)"));
    }

    // ------------------------------------------------------------- Adaptador de red

    /// <summary>
    /// Adaptadores con alguna descarga de proceso o ahorro de energia activo.
    ///
    /// No se mira un adaptador concreto sino cuantos tienen algo puesto, porque en un rig
    /// con Dante y NDI la respuesta correcta es «ninguno» y lo unico que hace falta saber
    /// es si eso se cumple y donde no.
    /// </summary>
    private static void ReadNetwork(List<SettingReading> readings)
    {
        try
        {
            ManagementScope scope = new(@"root\StandardCimv2");
            ObjectQuery query = new(
                "SELECT Name, RegistryKeyword, RegistryValue " +
                "FROM MSFT_NetAdapterAdvancedPropertySettingData");

            using ManagementObjectSearcher searcher = new(scope, query);

            List<NicAdvancedProperty> properties = new();

            foreach (ManagementBaseObject property in searcher.Get())
            {
                using (property)
                {
                    if (property["RegistryKeyword"] is not string keyword ||
                        property["Name"] is not string adapter)
                    {
                        continue;
                    }

                    // RegistryValue es un array de cadenas; «0» es desactivado.
                    properties.Add(new NicAdvancedProperty(adapter, keyword,
                        property["RegistryValue"] is string[] { Length: > 0 } values ? values[0] : null));
                }
            }

            // Que se apaga y que no lo decide Core, no esta lectura. La escritura usa la
            // misma politica, y esa es la unica forma de que la fila y el boton de
            // arreglarla no se separen.
            NicOffloadPlan plan = NicOffloadPolicy.Plan(properties);

            readings.Add(SettingReading.Of(
                SettingIds.NicOffloads, plan.Adapters.Count, plan.Describe()));

            ReadNicRegistrySettings(readings);
            ReadProductionPriority(readings);
            ReadPresentation(readings);
        }
        catch (ManagementException)
        {
            // root\StandardCimv2 no existe antes de Windows 8, y algunos drivers no
            // publican propiedades avanzadas.
            readings.Add(SettingReading.Missing(SettingIds.NicOffloads,
                "el driver no publica propiedades avanzadas"));
        }
    }


    /// <summary>
    /// Ahorro de energia y Nagle, por adaptador.
    ///
    /// Las dos ya se leian para la pagina de Red y se tiraban: entraban al modelo de
    /// interfaz y no las ensenaba nadie. Ahora tienen fila en el catalogo, que es donde se
    /// pueden auditar contra un objetivo.
    /// </summary>
    private static void ReadNicRegistrySettings(List<SettingReading> readings)
    {
        IReadOnlyList<NetworkInterfaceInfo> nics = new NetworkInterfaceProvider().GetInterfaces();

        // Solo las fisicas y activas. Un adaptador virtual o desconectado no tiene nada que
        // decir aqui, y contarlo llenaria la fila de ruido.
        List<NetworkInterfaceInfo> real = nics.Where(n => n.IsUp && n.IsPhysical).ToList();

        List<string> conAhorro = real
            .Where(n => n.PowerManagementEnabled != false)
            .Select(n => n.Name).ToList();

        // «Distinto de true» y no «igual a false»: si algun dia vuelve a haber un null por
        // no poder leer la clave, un adaptador sin dato tiene que contar como pendiente de
        // mirar y no como correcto. El verde se gana, no se hereda.
        List<string> conNagle = real
            .Where(n => n.NagleDisabled != true)
            .Select(n => n.Name).ToList();

        readings.Add(Nic(SettingIds.NicPowerManagement, real.Count, conAhorro));
        readings.Add(Nic(SettingIds.NicNagle, real.Count, conNagle));
    }

    /// <summary>
    /// Una fila por adaptador: cuantos infringen, y cuales.
    ///
    /// Sin ninguna tarjeta fisica activa no se dice «cero», se dice que no hay nada que
    /// mirar: un cero afirma que esta bien algo que ni siquiera existe.
    /// </summary>
    private static SettingReading Nic(string id, int total, IReadOnlyList<string> offenders)
    {
        if (total == 0)
        {
            return SettingReading.Missing(id, "sin tarjetas de red físicas activas");
        }

        return SettingReading.Of(id, offenders.Count,
            offenders.Count == 0
                ? "ninguno"
                : string.Join(", ", offenders.Take(3)) + (offenders.Count > 3 ? "…" : string.Empty));
    }

    /// <summary>
    /// Si las aplicaciones de produccion que estan corriendo van en la prioridad que les toca.
    ///
    /// Se mira lo que corre y no lo que esta instalado: un rig con vMix cerrado no tiene
    /// ningun problema, y decir que si es el falso positivo que hace que se deje de mirar la
    /// pantalla.
    /// </summary>
    private static void ReadProductionPriority(List<SettingReading> readings)
    {
        try
        {
            List<RunningProcess> running = new();

            foreach (global::System.Diagnostics.Process process in global::System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        string image = process.ProcessName + ".exe";

                        if (ProductionApps.Find(image) is null)
                        {
                            continue;
                        }

                        running.Add(new RunningProcess(image, Map(process.PriorityClass)));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        // El proceso ha terminado entre enumerarlo y preguntarle, o es de
                        // otra sesion y no deja mirarlo. Ninguna de las dos es un fallo.
                    }
                }
            }

            ProductionAppAudit audit = ProductionApps.Audit(running);

            readings.Add(SettingReading.Of(
                SettingIds.ProductionPriority, audit.BelowTarget.Count, audit.Describe()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            readings.Add(SettingReading.Missing(
                SettingIds.ProductionPriority, "no se ha podido enumerar los procesos"));
        }
    }

    private static ProcessPriority Map(global::System.Diagnostics.ProcessPriorityClass priority) => priority switch
    {
        global::System.Diagnostics.ProcessPriorityClass.Idle => ProcessPriority.Idle,
        global::System.Diagnostics.ProcessPriorityClass.BelowNormal => ProcessPriority.BelowNormal,
        global::System.Diagnostics.ProcessPriorityClass.AboveNormal => ProcessPriority.AboveNormal,
        global::System.Diagnostics.ProcessPriorityClass.High => ProcessPriority.High,
        global::System.Diagnostics.ProcessPriorityClass.RealTime => ProcessPriority.Realtime,
        _ => ProcessPriority.Normal,
    };


    /// <summary>
    /// Como presenta Windows cada frame: optimizaciones de pantalla completa y VRR.
    ///
    /// Los dos viven en la misma cadena del registro, asi que se leen de una vez. Ausente no
    /// es apagado: los dos vienen activados de fabrica, y una maquina donde nadie ha abierto
    /// nunca esa pantalla de Configuracion los tiene puestos.
    /// </summary>
    private static void ReadPresentation(List<SettingReading> readings)
    {
        RegistryValue value = RegistrySettingsReader.Probe(
            RegistryHive.CurrentUser, DirectXPreferencesKey, "DirectXUserGlobalSettings");

        if (value.Outcome == RegistryOutcome.Denied)
        {
            readings.Add(SettingReading.Missing(SettingIds.FullscreenOptimizations,
                "sin permiso para leer las preferencias de DirectX"));
            readings.Add(SettingReading.Missing(SettingIds.VariableRefreshRate,
                "sin permiso para leer las preferencias de DirectX"));
            return;
        }

        string? raw = value.AsString;

        readings.Add(Presentation(SettingIds.FullscreenOptimizations, raw,
            DirectXGlobalSettings.SwapEffectUpgrade));

        readings.Add(Presentation(SettingIds.VariableRefreshRate, raw,
            DirectXGlobalSettings.VariableRefreshRate));
    }

    private static SettingReading Presentation(string id, string? raw, string key)
    {
        bool efectivo = DirectXGlobalSettings.Effective(raw, key);

        // Se dice si el valor viene de la cadena o del valor de fabrica. Ensenar «activado»
        // sin mas en una maquina que nunca lo ha tocado invita a buscar donde se cambio, y
        // no se cambio en ninguna parte.
        string? display = DirectXGlobalSettings.Read(raw, key) is null
            ? (efectivo ? "activadas (valor por defecto)" : null)
            : null;

        return SettingReading.Of(id, efectivo, display);
    }

    // -------------------------------------------------------------------- Diagnóstico

    private static void ReadDiagnostics(List<SettingReading> readings)
    {
        readings.Add(ReadUnexpectedShutdowns());
        readings.Add(ReadDevicesWithError());
        readings.Add(ReadTimerResolution());
        readings.Add(ReadVirtualizationFirmware());
    }

    /// <summary>
    /// Apagados inesperados de los últimos 90 días: el 6008 lo escribe el registro de
    /// eventos al arrancar tras un apagado sucio, y el 41 lo escribe el kernel.
    ///
    /// No es una curiosidad. Un contador alto es evidencia documentada de que la
    /// alimentación del sitio no es de fiar, y eso vale en una conversación con el cliente.
    /// </summary>
    private static SettingReading ReadUnexpectedShutdowns()
    {
        try
        {
            const string query =
                "*[System[(EventID=6008 or EventID=41) and TimeCreated[timediff(@SystemTime) <= 7776000000]]]";

            EventLogReader reader = new(new EventLogQuery("System", PathType.LogName, query));

            int count = 0;
            while (count < 1000)
            {
                using global::System.Diagnostics.Eventing.Reader.EventRecord? record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                count++;
            }

            return SettingReading.Of(SettingIds.UnexpectedShutdowns, count,
                count == 0 ? "ninguno" : $"{count} en 90 días");
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            return SettingReading.Missing(SettingIds.UnexpectedShutdowns,
                "sin acceso al registro de eventos del sistema");
        }
    }

    /// <summary>
    /// Volumenes fijos distintos del de sistema con la indexacion activa. En C: es normal;
    /// en el disco donde grabas es el indexador leyendo mientras escribes.
    /// </summary>
    private static SettingReading ReadSearchIndexing()
    {
        try
        {
            string system = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

            using ManagementObjectSearcher searcher = new(
                "SELECT DriveLetter, IndexingEnabled FROM Win32_Volume WHERE DriveType = 3");

            List<string> indexed = new();

            foreach (ManagementBaseObject volume in searcher.Get())
            {
                using (volume)
                {
                    if (volume["DriveLetter"] is not string letter || letter.Length == 0)
                    {
                        continue;
                    }

                    if (string.Equals(letter + @"\", system, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (volume["IndexingEnabled"] is true)
                    {
                        indexed.Add(letter);
                    }
                }
            }

            return SettingReading.Of(SettingIds.SearchIndexing, indexed.Count,
                indexed.Count == 0 ? "ninguna" : string.Join(", ", indexed));
        }
        catch (ManagementException)
        {
            return SettingReading.Missing(SettingIds.SearchIndexing, "WMI no ha respondido");
        }
    }

    private static SettingReading ReadDevicesWithError()
    {
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity " +
                "WHERE ConfigManagerErrorCode <> 0");

            List<string> broken = new();

            foreach (ManagementBaseObject device in searcher.Get())
            {
                using (device)
                {
                    if (device["Name"] is string name)
                    {
                        broken.Add(name);
                    }
                }
            }

            return SettingReading.Of(SettingIds.DevicesWithError, broken.Count,
                broken.Count == 0
                    ? "ninguno"
                    : string.Join(", ", broken.Take(4)) + (broken.Count > 4 ? "…" : string.Empty));
        }
        catch (ManagementException)
        {
            return SettingReading.Missing(SettingIds.DevicesWithError, "WMI no ha respondido");
        }
    }

    private static SettingReading ReadTimerResolution()
    {
        // Las tres salen en unidades de 100 ns.
        if (NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current) != 0)
        {
            return SettingReading.Missing(SettingIds.TimerResolution, "NtQueryTimerResolution ha fallado");
        }

        double currentMs = current / 10_000.0;

        return SettingReading.Of(SettingIds.TimerResolution, currentMs.ToString("0.###"),
            $"{currentMs:0.###} ms (mín {maximum / 10_000.0:0.###}, máx {minimum / 10_000.0:0.###})");
    }

    private static SettingReading ReadVirtualizationFirmware()
    {
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");

            foreach (ManagementBaseObject processor in searcher.Get())
            {
                using (processor)
                {
                    if (processor["VirtualizationFirmwareEnabled"] is bool enabled)
                    {
                        return SettingReading.Of(SettingIds.VirtualizationFirmware, enabled);
                    }
                }
            }

            return SettingReading.Missing(SettingIds.VirtualizationFirmware, "el procesador no lo reporta");
        }
        catch (ManagementException)
        {
            return SettingReading.Missing(SettingIds.VirtualizationFirmware, "WMI no ha respondido");
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(
        out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    // ------------------------------------------------------------------------- Claves

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    /// <summary>No es la de arriba: HiberbootEnabled y HibernateEnabled viven en claves distintas.</summary>
    private const string HibernationKey = @"SYSTEM\CurrentControlSet\Control\Power";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string MemoryManagementKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    private const string MmcssKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ProAudioKey = MmcssKey + @"\Tasks\Pro Audio";

    private const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string HvciKey =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    private const string VisualEffectsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string GameDvrPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string DirectXPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    private const string WindowsUpdateSettingsKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string WindowsUpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string WindowsUpdateAuPolicyKey = WindowsUpdatePolicyKey + @"\AU";
    private const string RebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string CbsRebootPendingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";

    private const string DeliveryOptimizationPolicyKey =
        @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string DeliveryOptimizationConfigKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";

    private const string QuietHoursRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";
    private const string PushNotificationsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string StoreUpdateKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsStore\WindowsUpdate";
    private const string StorageSenseKey = @"SOFTWARE\Policies\Microsoft\Windows\StorageSense";

    private const string DefenderFeaturesKey = @"SOFTWARE\Microsoft\Windows Defender\Features";
    private const string DefenderPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    private const string ControlledFolderAccessKey =
        @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";

    private const string BackgroundAppsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string DeviceInstallerKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer";
    private const string WinlogonKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    private const string DesktopKey = @"Control Panel\Desktop";
    private const string PoliciesSystemKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
}
