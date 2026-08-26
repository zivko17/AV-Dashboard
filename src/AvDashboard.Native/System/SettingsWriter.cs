using System.ServiceProcess;
using AvDashboard.Core.Models;

// System.ServiceProcess trae su propio ServiceStartMode; aqui gana el nuestro, igual que en
// WindowsServiceReader.
using ServiceStartMode = AvDashboard.Core.Models.ServiceStartMode;
using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using AvDashboard.Core.Networking;
using AvDashboard.Core.Settings;
using AvDashboard.Native.Network;
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
        SettingIds.UltimateAvailable,
        SettingIds.Hibernation,
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
        SettingIds.VisualEffects,
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

        // Red
        SettingIds.NicOffloads,

        SettingIds.NicPowerManagement,
        SettingIds.NicNagle,

        // Presentación de vídeo
        SettingIds.FullscreenOptimizations,
        SettingIds.VariableRefreshRate,

        // Memoria
        SettingIds.PagefileFixed,

        // Sincronía de hora
        SettingIds.TimeServiceStartup,
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
                SettingIds.UltimateAvailable => ApplyUltimateAvailable(value, dryRun),
                SettingIds.Hibernation => ApplyHibernation(value, dryRun),

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

                SettingIds.VisualEffects => ApplyVisualEffects(value, dryRun),

                SettingIds.Transparency => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency"),
                SettingIds.GameMode => Flag(id, value, dryRun,
                    RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled"),
                SettingIds.GameDvr => ApplyGameDvr(value, dryRun),

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

                SettingIds.NicOffloads => ApplyNicOffloads(value, dryRun),

                SettingIds.NicPowerManagement => ApplyNic(
                    SettingIds.NicPowerManagement, value, dryRun,
                    NicRegistryWriter.DisablePowerManagement,
                    "El ahorro de energía de la NIC solo se apaga desde aquí. Volver a " +
                    "encenderlo se hace en el Administrador de dispositivos, y a sabiendas."),

                SettingIds.NicNagle => ApplyNic(
                    SettingIds.NicNagle, value, dryRun,
                    NicRegistryWriter.DisableNagle,
                    "Nagle solo se desactiva desde aquí. Volver a activarlo es borrar dos " +
                    "valores del registro a mano."),

                SettingIds.FullscreenOptimizations => ApplyPresentation(
                    SettingIds.FullscreenOptimizations,
                    DirectXGlobalSettings.SwapEffectUpgrade, value, dryRun),

                SettingIds.VariableRefreshRate => ApplyPresentation(
                    SettingIds.VariableRefreshRate,
                    DirectXGlobalSettings.VariableRefreshRate, value, dryRun),

                SettingIds.PagefileFixed => ApplyPagefile(value, dryRun),

                SettingIds.TimeServiceStartup => ApplyTimeService(value, dryRun),

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

    /// <summary>
    /// Apaga las descargas del adaptador de red.
    ///
    /// El unico valor que se acepta es el objetivo del catalogo, «0»: no hay un caso de uso
    /// para volver a encenderlas desde aqui, y ofrecer el camino de vuelta seria ofrecer un
    /// boton para romper Dante.
    ///
    /// Corta el enlace un segundo mientras se aplica. El catalogo lo declara Disruptive por
    /// eso, y el barrido de «arregla lo que este en rojo» lo deja fuera: se toca de uno en
    /// uno, o dentro de un perfil, que es algo que se aplica antes de que empiece el show.
    /// </summary>
    private static SettingWriteResult ApplyNicOffloads(string value, bool dryRun)
    {
        if (!string.Equals(value, NicOffloadPolicy.Disabled, StringComparison.Ordinal))
        {
            return Failed(SettingIds.NicOffloads,
                "Las descargas de la NIC solo se apagan desde aquí. Volver a encenderlas se " +
                "hace en el Administrador de dispositivos, y a sabiendas.");
        }

        NicOffloadResult result = NicOffloadWriter.DisableAll(dryRun);

        if (!result.Success)
        {
            return Failed(SettingIds.NicOffloads, result.Failures.Count > 0
                ? result.Failures[0]
                : $"Quedan descargas activas en {result.Remaining.Describe()}. " +
                  "Suele hacer falta que el agente corra como servicio.");
        }

        // El valor de esta fila es «cuantos adaptadores tienen algo encendido». En seco,
        // Remaining es lo que hay ahora y el resultado seria cero: informar de «1 → 1»
        // dejaria al usuario creyendo que el ensayo no iba a cambiar nada.
        string previous = Count(result.Remaining.Adapters.Count);
        string applied = result.DryRun ? Count(0) : Count(result.Remaining.Adapters.Count);

        return Ok(SettingIds.NicOffloads, previous, applied, dryRun);
    }

    private static string Count(int value) =>
        value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Game DVR, mirando antes si una directiva de maquina lo tiene prohibido.
    ///
    /// Se lee de dos sitios y la directiva gana. Escribiendo solo la preferencia del
    /// usuario, la clave cambiaba, el actuador devolvia exito y el Timeline anotaba
    /// «off → on»… mientras la maquina seguia con Game DVR apagado porque la directiva lo
    /// prohibia. Una escritura que ocurre y no sirve de nada es tan mentira como una que no
    /// ocurre: lo que se anota tiene que ser lo que pasa.
    /// </summary>
    private static SettingWriteResult ApplyGameDvr(string value, bool dryRun)
    {
        bool? policy = RegistrySettingsReader.ReadBool(
            RegistryHive.LocalMachine, GameDvrPolicyKey, "AllowGameDVR");

        if (PolicyOverride.IsBlockedByPolicy(policy, Boolean(value)))
        {
            return Failed(SettingIds.GameDvr, PolicyOverride.BlockedMessage(
                SettingsCatalog.Find(SettingIds.GameDvr)?.Name ?? "Game DVR"));
        }

        return Flag(SettingIds.GameDvr, value, dryRun,
            RegistryHive.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled");
    }

    /// <summary>
    /// Enciende o apaga uno de los interruptores de presentacion de DirectX.
    ///
    /// Los tres viven en la misma cadena del registro, asi que escribir uno significa
    /// reescribirla entera conservando los otros. La composicion la hace Core, que es donde
    /// esta probada: hacerlo a mano aqui es la manera de llevarse por delante la preferencia
    /// de GPU por aplicacion que el usuario tenga puesta al lado.
    ///
    /// No corta nada mientras se aplica, pero **no surte efecto hasta que la aplicacion
    /// vuelve a crear su swapchain**: con Resolume abierto hay que reiniciarlo.
    /// </summary>
    private static SettingWriteResult ApplyPresentation(
        string id, string key, string value, bool dryRun)
    {
        string? raw = RegistrySettingsReader.ReadString(
            RegistryHive.CurrentUser, DirectXPreferencesKey, "DirectXUserGlobalSettings");

        bool previous = DirectXGlobalSettings.Effective(raw, key);
        bool target = Boolean(value);

        string previousText = previous ? SettingReading.BooleanOn : SettingReading.BooleanOff;
        string targetText = target ? SettingReading.BooleanOn : SettingReading.BooleanOff;

        if (dryRun)
        {
            return Ok(id, previousText, targetText, dryRun: true);
        }

        using (RegistryKey subKey = Open(RegistryHive.CurrentUser, DirectXPreferencesKey))
        {
            subKey.SetValue("DirectXUserGlobalSettings",
                DirectXGlobalSettings.With(raw, key, target), RegistryValueKind.String);
        }

        // Se comprueba releyendo. Es la regla que costo siete esquemas de energia basura.
        string? after = RegistrySettingsReader.ReadString(
            RegistryHive.CurrentUser, DirectXPreferencesKey, "DirectXUserGlobalSettings");

        return DirectXGlobalSettings.Effective(after, key) == target
            ? Ok(id, previousText, targetText)
            : Failed(id, "El valor no se ha quedado escrito.");
    }

    /// <summary>
    /// Los dos ajustes de adaptador que se escriben en el registro.
    ///
    /// Comparten forma con el de las descargas: solo se acepta apagarlos, se aplica a todas
    /// las tarjetas fisicas activas, y se comprueba releyendo en vez de creerse la llamada.
    /// Ofrecer el camino de vuelta seria ofrecer un boton para romper Dante.
    /// </summary>
    private static SettingWriteResult ApplyNic(
        string id, string value, bool dryRun, Func<bool, NicWriteResult> apply, string refusal)
    {
        // Se compara contra «0» y no con Boolean(), que espera «on»/«off». Estas filas se
        // miden en «cuantos adaptadores infringen», asi que su valor es un numero. Con
        // Boolean() el guardarrail no guardaba nada: mandar «1» no se rechazaba, se colaba
        // e intentaba escribir, y solo fallaba despues por falta de permisos.
        if (!string.Equals(value, "0", StringComparison.Ordinal))
        {
            return Failed(id, refusal);
        }

        NicWriteResult result = apply(dryRun);

        if (!result.Success)
        {
            return Failed(id, result.Failures.Count > 0
                ? result.Failures[0]
                : $"Siguen mal: {string.Join(", ", result.Remaining)}.");
        }

        // El valor de estas filas es «cuantos adaptadores infringen». En seco, lo que se
        // tocaria; aplicado, lo que queda, que si ha ido bien es cero.
        string previous = Count(result.DryRun ? result.Touched.Count : result.Touched.Count);
        string applied = Count(result.DryRun ? 0 : result.Remaining.Count);

        return Ok(id, previous, applied, dryRun);
    }

    /// <summary>
    /// Fija el tamano del archivo de paginacion, o lo devuelve a Windows.
    ///
    /// Se escribe la lista del registro y nada mas. Es exactamente lo que hace el dialogo de
    /// memoria virtual al desmarcar «administrar automaticamente»: la entrada comodin
    /// «?:\pagefile.sys» **es** esa casilla, no un reflejo suyo, asi que sustituirla por una
    /// ruta con dos numeros la desmarca. Por eso no hace falta tocar
    /// Win32_ComputerSystem.AutomaticManagedPagefile, que ademas obligaria a traerse WMI
    /// para escribir algo que ya esta escrito.
    ///
    /// **No surte efecto hasta reiniciar.** El archivo actual sigue en marcha; el nuevo
    /// tamano se reserva en el siguiente arranque. Se dice en el mensaje porque una fila que
    /// se pone verde y un rig que se comporta igual que antes es la manera de perder la
    /// confianza en el panel entero.
    /// </summary>
    private static SettingWriteResult ApplyPagefile(string value, bool dryRun)
    {
        string id = SettingIds.PagefileFixed;

        IReadOnlyList<string>? current = RegistrySettingsReader.ReadMultiString(
            RegistryHive.LocalMachine, MemoryManagementKey, "PagingFiles");

        if (current is null)
        {
            return Failed(id, "No se ha podido leer la configuración de memoria actual.");
        }

        IReadOnlyList<PagingFileEntry> entries = PagingFile.Parse(current);
        string previous = PagingFile.Canonical(PagingFile.Classify(entries));

        string[] target;

        if (string.Equals(value, PagingFile.Target, StringComparison.Ordinal))
        {
            // El disco: el que ya lo aloja si hay uno elegido, y si no el del sistema. No se
            // mueve a otra unidad por iniciativa propia: en un rig la segunda unidad suele
            // ser la de medios, y ponerselo encima cambia un problema por otro peor.
            string drive = entries.FirstOrDefault(e => !e.IsAutomatic)?.Drive
                ?? Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\')
                ?? "C:";

            long free;

            try
            {
                free = new DriveInfo(drive).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException
                                          or UnauthorizedAccessException)
            {
                return Failed(id, $"No se ha podido mirar el espacio libre de {drive}.");
            }

            int? size = PagingFile.RecommendMb(
                SystemTelemetryProvider.TotalPhysicalMemoryBytes(), free);

            if (size is null)
            {
                return Failed(id,
                    $"No hay sitio en {drive} para un archivo de paginación decente. "
                    + "Libera espacio antes de fijarlo.");
            }

            target = new[]
            {
                new PagingFileEntry($@"{drive}\pagefile.sys", size.Value, size.Value).ToString(),
            };
        }
        else if (string.Equals(value, PagingFile.SystemManagedValue, StringComparison.Ordinal))
        {
            // El camino de vuelta si existe, al reves que en las filas de la NIC: devolverselo
            // a Windows no rompe nada y es lo que hace falta para que deshacer funcione.
            target = new[] { @"?:\pagefile.sys" };
        }
        else
        {
            return Failed(id,
                "Desde aquí solo se fija el tamaño o se le devuelve a Windows. "
                + "Desactivar la paginación del todo no se ofrece: Windows cuenta memoria "
                + "reservada y las aplicaciones fallarían al reservar antes de llenar la RAM.");
        }

        string applied = PagingFile.Canonical(PagingFile.Classify(target));

        if (dryRun)
        {
            return Ok(id, previous, applied, dryRun: true);
        }

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);

            using RegistryKey? key = baseKey.OpenSubKey(MemoryManagementKey, writable: true);

            if (key is null)
            {
                return Failed(id, "No existe la clave de memoria del sistema.");
            }

            key.SetValue("PagingFiles", target, RegistryValueKind.MultiString);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                      or global::System.Security.SecurityException)
        {
            return Failed(id, "Hace falta que el agente corra como servicio para tocar esto.");
        }

        // Se relee. Es la regla que costo siete esquemas de energia basura.
        IReadOnlyList<string>? after = RegistrySettingsReader.ReadMultiString(
            RegistryHive.LocalMachine, MemoryManagementKey, "PagingFiles");

        if (PagingFile.Canonical(PagingFile.Classify(after)) != applied)
        {
            return Failed(id, "El valor no se ha quedado escrito.");
        }

        return new SettingWriteResult
        {
            Id = id,
            Success = true,
            Message = "Aplicado. No surte efecto hasta reiniciar: el archivo que hay ahora "
                + "sigue funcionando y el nuevo tamaño se reserva en el próximo arranque.",
            PreviousValue = previous,
            AppliedValue = applied,
        };
    }

    /// <summary>
    /// Pone W32Time en automatico y lo arranca, o lo devuelve a manual.
    ///
    /// El modo de arranque se cambia por WMI y no escribiendo «Start» en el registro. Las
    /// dos cosas funcionan, pero el SCM tiene su propia copia en memoria: por el registro el
    /// valor queda bien y el servicio se sigue comportando como antes hasta el siguiente
    /// arranque, que es la clase de exito a medias que este proyecto ya ha pagado una vez.
    ///
    /// Arrancarlo corrige el reloj, y si esta desviado la correccion es un salto. Por eso la
    /// fila es disruptiva: se aplica antes del show, no durante.
    /// </summary>
    private static SettingWriteResult ApplyTimeService(string value, bool dryRun)
    {
        string id = SettingIds.TimeServiceStartup;

        string target = value switch
        {
            TimeSync.TargetStartup => "Automatic",
            TimeSync.ManualStartup => "Manual",
            _ => string.Empty,
        };

        if (target.Length == 0)
        {
            return Failed(id,
                "Desde aquí solo se pone en automático o se devuelve a manual. Desactivar "
                + "el servicio de hora dejaría el reloj yendo solo, que es peor que el "
                + "problema que se estaba arreglando.");
        }

        (ServiceStartMode Mode, bool Running)? before =
            WindowsServiceReader.Inspect("W32Time");

        if (before is null)
        {
            return Failed(id, "No se ha podido consultar el servicio W32Time.");
        }

        string? previous = TimeSync.StartupCanonical(before.Value.Mode);

        if (dryRun)
        {
            return Ok(id, previous, value, dryRun: true);
        }

        try
        {
            using ManagementObject service = new(
                new ManagementPath("Win32_Service.Name='W32Time'"));

            object result = service.InvokeMethod(
                "ChangeStartMode", new object[] { target })!;

            uint code = Convert.ToUInt32(result, CultureInfo.InvariantCulture);

            if (code != 0)
            {
                // 2 es acceso denegado, y es el que se va a ver mientras el agente no sea
                // servicio. El resto se dice con su numero: son raros y sin el numero no hay
                // por donde empezar a mirar.
                return Failed(id, code == 2
                    ? "Hace falta que el agente corra como servicio para tocar esto."
                    : $"Windows ha rechazado el cambio de arranque (código {code}).");
            }
        }
        catch (ManagementException)
        {
            return Failed(id, "No se ha podido cambiar el arranque del servicio W32Time.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(id, "Hace falta que el agente corra como servicio para tocar esto.");
        }

        string arrancado = string.Empty;

        if (target == "Automatic" && !before.Value.Running)
        {
            try
            {
                using ServiceController controller = new("W32Time");
                controller.Start();
                controller.WaitForStatus(
                    ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));

                arrancado = " Arrancado: el reloj se pone en hora en cuanto conteste el "
                    + "servidor.";
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                          or global::System.ServiceProcess.TimeoutException)
            {
                // El arranque en automatico ya esta escrito y eso es lo que pedia la fila.
                // Que ademas no haya arrancado ahora es una noticia, no un fracaso, y
                // decirlo es mas util que revertir el cambio bueno.
                arrancado = " No ha arrancado ahora; lo hará en el próximo reinicio.";
            }
        }

        // Se relee del SCM, que es quien manda, y no del registro.
        (ServiceStartMode Mode, bool Running)? check = WindowsServiceReader.Inspect("W32Time");

        if (check is null)
        {
            return Failed(id, "No se ha podido comprobar el servicio después de escribir.");
        }

        string? after = TimeSync.StartupCanonical(check.Value.Mode);

        if (after != value)
        {
            return Failed(id, "El arranque no se ha quedado cambiado.");
        }

        return new SettingWriteResult
        {
            Id = id,
            Success = true,
            Message = "Aplicado." + arrancado,
            PreviousValue = previous,
            AppliedValue = after,
        };
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

    /// <summary>
    /// Deja Ultimate Performance presente en la lista de planes.
    ///
    /// Es la unica fila del catalogo cuyo objetivo es «que exista», no «que valga tal
    /// cosa»: sin el esquema duplicado, el objetivo de la fila de arriba —el plan activo—
    /// no se puede cumplir en las ediciones de Windows que no lo traen. Apagarlo no se
    /// ofrece: borrar un esquema de energia del que puede colgar el plan activo no es algo
    /// que deba hacer un interruptor.
    /// </summary>
    private static SettingWriteResult ApplyUltimateAvailable(string value, bool dryRun)
    {
        bool present = PowerConfiguration.IsUltimatePerformanceAvailable();
        string previous = present ? SettingReading.BooleanOn : SettingReading.BooleanOff;

        if (!Boolean(value))
        {
            return Failed(SettingIds.UltimateAvailable,
                "Quitar el esquema Ultimate Performance no se hace desde aquí: si es el " +
                "plan activo, la máquina se queda sin plan.");
        }

        if (present)
        {
            return Ok(SettingIds.UltimateAvailable, previous, SettingReading.BooleanOn, dryRun);
        }

        if (dryRun)
        {
            return Ok(SettingIds.UltimateAvailable, previous, SettingReading.BooleanOn, dryRun: true);
        }

        return PowerConfiguration.TryDuplicateUltimatePerformance()
            ? Ok(SettingIds.UltimateAvailable, previous, SettingReading.BooleanOn)
            : Failed(SettingIds.UltimateAvailable,
                "Windows no ha dejado duplicar el esquema. Hace falta que el agente corra " +
                "como servicio.");
    }

    /// <summary>
    /// Enciende o apaga la hibernacion.
    ///
    /// Va por la API de energia y no escribiendo HibernateEnabled: el registro deja el
    /// valor puesto pero el hiberfil.sys sigue ocupando tanto como la RAM, y el inicio
    /// rapido puede seguir usandolo. Es la diferencia entre que la fila se ponga verde y
    /// que la maquina arranque limpia de verdad.
    /// </summary>
    private static SettingWriteResult ApplyHibernation(string value, bool dryRun)
    {
        bool target = Boolean(value);

        string? previous = RegistrySettingsReader
            .ReadBool(RegistryHive.LocalMachine, HibernationKey, "HibernateEnabled") switch
        {
            true => SettingReading.BooleanOn,
            false => SettingReading.BooleanOff,
            null => null,
        };

        if (dryRun)
        {
            return Ok(SettingIds.Hibernation, previous,
                target ? SettingReading.BooleanOn : SettingReading.BooleanOff, dryRun: true);
        }

        return PowerConfiguration.TrySetHibernation(target)
            ? Ok(SettingIds.Hibernation, previous,
                target ? SettingReading.BooleanOn : SettingReading.BooleanOff)
            : Failed(SettingIds.Hibernation,
                "Windows ha rechazado el cambio. Hace falta que el agente corra como servicio.");
    }

    /// <summary>
    /// Efectos visuales.
    ///
    /// El catalogo ya declaraba un actuador para esta fila, pero el conmutador no la
    /// tenia: la fila salia como no escribible mientras el perfil «Evento» prometia
    /// cambiarla. El valor viaja con el nombre que usa Windows y no con su numero, que es
    /// como se lee en la vista, asi que la traduccion vive aqui.
    /// </summary>
    private static SettingWriteResult ApplyVisualEffects(string value, bool dryRun)
    {
        uint? target = value switch
        {
            "LetWindowsChoose" => 0u,
            "BestAppearance" => 1u,
            "BestPerformance" => 2u,
            "Custom" => 3u,
            _ => null,
        };

        if (target is not uint setting)
        {
            return Failed(SettingIds.VisualEffects, $"«{value}» no es un modo de efectos visuales.");
        }

        string? before = RegistrySettingsReader
            .ReadInt(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting") switch
        {
            0 => "LetWindowsChoose",
            1 => "BestAppearance",
            2 => "BestPerformance",
            3 => "Custom",
            _ => null,
        };

        if (dryRun)
        {
            return Ok(SettingIds.VisualEffects, before, value, dryRun: true);
        }

        WriteDword(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting", setting);

        return Ok(SettingIds.VisualEffects, before, value);
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

    /// <summary>Ver la nota de SettingsProbe: HibernateEnabled no cuelga de la de arriba.</summary>
    private const string HibernationKey = @"SYSTEM\CurrentControlSet\Control\Power";

    private const string VisualEffectsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string MmcssKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ProAudioKey = MmcssKey + @"\Tasks\Pro Audio";
    private const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string GameDvrPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string DirectXPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string MemoryManagementKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
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
