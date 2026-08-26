using AvDashboard.Core.Profiles;

namespace AvDashboard.Core.Settings;

/// <summary>
/// El catálogo de ajustes del sistema con su valor objetivo en un rig de producción.
///
/// Hasta ahora M1 leía una docena de valores y los pintaba. Eso responde a «cómo está la
/// máquina» pero no a la pregunta que de verdad te haces a las siete y media: «qué está mal
/// aquí». La diferencia entre las dos es exactamente esta tabla: cada fila sabe cuál es el
/// valor que quieres en un rig, cuánto mueve la aguja, y qué contrapartida tiene tocarlo.
///
/// Vive en Core y no en la capa nativa por dos razones. Una, es lógica pura: comparar lo
/// leído contra lo esperado se prueba entero sin tocar Windows. Y dos, así añadir un ajuste
/// es una fila aquí más una línea de lectura allí, en vez de repartir el criterio por tres
/// capas.
///
/// Un ajuste sin <see cref="SettingDefinition.AcceptableValues"/> es informativo: se enseña
/// pero nunca se pinta en rojo. Es deliberado para los que no tienen respuesta universal.
/// </summary>
public static class SettingsCatalog
{
    private static readonly IReadOnlyList<SettingDefinition> Definitions = new SettingDefinition[]
    {
        // ------------------------------------------------------------------ Energía
        new()
        {
            Id = SettingIds.PowerPlan,
            Group = SettingGroup.Power,
            Name = "Plan de energía activo",
            Source = "PowerGetActiveScheme",
            AcceptableValues = new[] { "ultimate", "high" },
            TargetDescription = "Ultimate Performance o Alto rendimiento",
            Impact = SettingImpact.High,
            ActionId = ActionIds.PowerPlan,
            Rationale = "Equilibrado baja la frecuencia justo cuando el render la necesita.",
        },
        new()
        {
            Id = SettingIds.UltimateAvailable,
            Group = SettingGroup.Power,
            Name = "Ultimate Performance disponible",
            Source = "GUID e9a42b02-d5df-448d-aa00-03f14749eb61",
            AcceptableValues = new[] { SettingReading.BooleanOn },
            TargetDescription = "Presente",
            Impact = SettingImpact.Medium,
            Rationale = "No viene de serie en todas las ediciones; se duplica con powercfg.",
        },
        new()
        {
            Id = SettingIds.ProcessorMin,
            Group = SettingGroup.Power,
            Name = "Estado mínimo del procesador (CA)",
            Source = "PROCTHROTTLEMIN",
            AcceptableValues = new[] { "100" },
            TargetDescription = "100 %",
            Impact = SettingImpact.High,
            ActionId = ActionIds.ProcessorState,
            Rationale = "Subir de frecuencia tarda; en un pico de render ya has soltado el frame.",
        },
        new()
        {
            Id = SettingIds.ProcessorMax,
            Group = SettingGroup.Power,
            Name = "Estado máximo del procesador (CA)",
            Source = "PROCTHROTTLEMAX",
            AcceptableValues = new[] { "100" },
            TargetDescription = "100 %",
            Impact = SettingImpact.Medium,
            ActionId = ActionIds.ProcessorState,
            Rationale = "Un techo por debajo de 100 limita el pico sin avisar.",
        },
        new()
        {
            Id = SettingIds.CoreParking,
            Group = SettingGroup.Power,
            Name = "Core parking",
            Source = "GUID 0cc5b647-c1df-4637-891a-dec35c318583",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Todos los núcleos despiertos",
            Impact = SettingImpact.High,
            Rationale = "Despertar un núcleo aparcado cuesta latencia que el audio no perdona.",
        },
        new()
        {
            Id = SettingIds.UsbSelectiveSuspend,
            Group = SettingGroup.Power,
            Name = "Suspensión selectiva USB",
            Source = "GUID 48e6b7a6-50f5-4782-a5d4-53bb8f07e226",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            ActionId = ActionIds.UsbSelectiveSuspend,
            Rationale = "El asesino silencioso de interfaces de audio y dongles.",
        },
        new()
        {
            Id = SettingIds.PcieAspm,
            Group = SettingGroup.Power,
            Name = "PCIe ASPM",
            Source = "GUID ee12f906-d277-404b-b6da-e5fa1a576df5",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            Rationale = "El ahorro de energía del bus alcanza a la captura y a la NIC.",
        },
        new()
        {
            Id = SettingIds.DiskTimeout,
            Group = SettingGroup.Power,
            Name = "Apagar disco duro tras",
            Source = "GUID 6738e2c4-e8a5-4a42-b16a-e040e769756e",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Nunca",
            Impact = SettingImpact.Medium,
            Rationale = "Un disco dormido en mitad de una grabación es un corte.",
        },
        new()
        {
            Id = SettingIds.SleepTimeout,
            Group = SettingGroup.Power,
            Name = "Suspender el equipo (CA)",
            Source = "GUID 29f6c1db-86da-48c5-9fdb-f2b67b1f44da",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Nunca",
            Impact = SettingImpact.High,
            Rationale = "No hace falta explicar qué pasa si el rig se duerme durante el show.",
        },
        new()
        {
            Id = SettingIds.DisplayTimeout,
            Group = SettingGroup.Power,
            Name = "Apagar pantalla (CA)",
            Source = "GUID 3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Nunca",
            Impact = SettingImpact.High,
            Rationale = "Apagar la salida puede renegociar el enlace con el procesador LED.",
        },
        new()
        {
            Id = SettingIds.Hibernation,
            Group = SettingGroup.Power,
            Name = "Hibernación",
            Source = "powercfg /a",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivada",
            Impact = SettingImpact.Medium,
            Rationale = "Va de la mano de Fast Startup y del arranque limpio tras un corte.",
        },
        new()
        {
            Id = SettingIds.WakeTimers,
            Group = SettingGroup.Power,
            Name = "Temporizadores de reactivación",
            Source = "GUID bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivados",
            Impact = SettingImpact.Medium,
            Rationale = "Una tarea programada puede despertar la máquina a mitad de montaje.",
        },

        new()
        {
            Id = SettingIds.WakeArmedTasks,
            Group = SettingGroup.Power,
            Name = "Tareas programadas que pueden despertar el equipo",
            Source = "TaskScheduler COM, Settings.WakeToRun",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Ninguna",
            Impact = SettingImpact.High,
            Rationale = "El interruptor general de temporizadores no dice cuáles son. Estas son las que sacan la máquina de suspensión a las cuatro de la mañana, o la despiertan en mitad del montaje.",
        },

        // --------------------------------------------------------- Kernel y rendimiento
        new()
        {
            Id = SettingIds.MmcssResponsiveness,
            Group = SettingGroup.Kernel,
            Name = "MMCSS SystemResponsiveness",
            Source = @"HKLM\...\Multimedia\SystemProfile",
            AcceptableValues = new[] { "0", "10" },
            TargetDescription = "10, o 0 en rig dedicado",
            Impact = SettingImpact.High,
            ActionId = ActionIds.MmcssResponsiveness,
            Rationale = "Reserva de CPU para tareas multimedia: el ajuste de más impacto real en NDI y DVS.",
        },
        new()
        {
            Id = SettingIds.NetworkThrottling,
            Group = SettingGroup.Kernel,
            Name = "NetworkThrottlingIndex",
            Source = @"HKLM\...\Multimedia\SystemProfile",
            AcceptableValues = new[] { "4294967295" },
            TargetDescription = "0xFFFFFFFF (sin límite)",
            Impact = SettingImpact.High,
            ActionId = ActionIds.NetworkThrottling,
            Rationale = "Por defecto Windows limita a 10 000 paquetes por segundo cuando hay multimedia activo.",
        },
        new()
        {
            Id = SettingIds.ProAudioPriority,
            Group = SettingGroup.Kernel,
            Name = "Perfil MMCSS «Pro Audio»",
            Source = @"...\SystemProfile\Tasks\Pro Audio",
            AcceptableValues = new[] { "1" },
            TargetDescription = "Priority 1",
            Impact = SettingImpact.High,
            Rationale = "Es la clase de planificación que piden los drivers ASIO y DVS.",
        },
        new()
        {
            Id = SettingIds.PrioritySeparation,
            Group = SettingGroup.Kernel,
            Name = "Win32PrioritySeparation",
            Source = @"HKLM\SYSTEM\CCS\Control\PriorityControl",
            AcceptableValues = new[] { "38", "24" },
            TargetDescription = "0x26 (programas) o 0x18",
            Impact = SettingImpact.Medium,
            Rationale = "Reparto de quantum entre primer plano y fondo.",
        },
        new()
        {
            Id = SettingIds.PowerThrottling,
            Group = SettingGroup.Kernel,
            Name = "Power Throttling",
            Source = @"HKLM\SYSTEM\CCS\Control\Power\PowerThrottling",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            Rationale = "Windows decide por su cuenta ralentizar procesos que cree en segundo plano.",
        },
        new()
        {
            Id = SettingIds.VisualEffects,
            Group = SettingGroup.Kernel,
            Name = "Efectos visuales",
            Source = "VisualFXSetting",
            AcceptableValues = new[] { "BestPerformance" },
            TargetDescription = "Mejor rendimiento",
            Impact = SettingImpact.Medium,
            ActionId = ActionIds.VisualEffects,
            Rationale = "Animaciones y sombras compiten con el compositor por la GPU.",
        },
        new()
        {
            Id = SettingIds.Transparency,
            Group = SettingGroup.Kernel,
            Name = "Transparencia",
            Source = @"...\Themes\Personalize\EnableTransparency",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivada",
            Impact = SettingImpact.Low,
            Rationale = "Trabajo de composición gratuito en cada repintado.",
        },
        new()
        {
            Id = SettingIds.Hags,
            Group = SettingGroup.Kernel,
            Name = "Planificación por hardware de GPU (HAGS)",
            Source = @"HKLM\SYSTEM\CCS\Control\GraphicsDrivers\HwSchMode",
            TargetDescription = "Sin objetivo: probar y medir",
            Impact = SettingImpact.Medium,
            Risk = SettingRisk.Tradeoff,
            Rationale = "En unos rigs mejora NVENC y en otros mete microcortes. No hay respuesta universal.",
        },
        new()
        {
            Id = SettingIds.GameMode,
            Group = SettingGroup.Kernel,
            Name = "Game Mode",
            Source = @"HKCU\Software\Microsoft\GameBar",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.Medium,
            ActionId = ActionIds.GameBar,
            Rationale = "Reasigna recursos según su idea de qué es el juego en primer plano.",
        },
        new()
        {
            Id = SettingIds.GameDvr,
            Group = SettingGroup.Kernel,
            Name = "Game DVR / grabación en segundo plano",
            Source = @"HKCU\System\GameConfigStore\GameDVR_Enabled",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            Rationale = "Graba en segundo plano usando el mismo NVENC que necesita tu stream.",
        },
        new()
        {
            Id = SettingIds.BackgroundApps,
            Group = SettingGroup.Kernel,
            Name = "Aplicaciones en segundo plano",
            Source = @"HKCU\...\BackgroundAccessApplications\GlobalUserDisabled",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivadas",
            Impact = SettingImpact.Medium,
            Rationale = "Aplicaciones de la Store despertándose solas a sincronizar mientras el rig renderiza.",
        },
        new()
        {
            Id = SettingIds.MemoryIntegrity,
            Group = SettingGroup.Kernel,
            Name = "Integridad de memoria (HVCI / VBS)",
            Source = @"HKLM\...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
            TargetDescription = "Sin objetivo automático: decisión manual",
            Impact = SettingImpact.High,
            Risk = SettingRisk.Security,
            Rationale = "Desactivarlo mejora latencia de forma medible y baja la seguridad de la máquina de forma real.",
        },
        new()
        {
            Id = SettingIds.CpuMitigations,
            Group = SettingGroup.Kernel,
            Name = "Mitigaciones Spectre/Meltdown",
            Source = "FeatureSettingsOverride",
            TargetDescription = "Sin objetivo automático: decisión manual",
            Impact = SettingImpact.High,
            Risk = SettingRisk.Security,
            Rationale = "Mismo caso que HVCI. En una máquina que también usas para banca, se dejan como están.",
        },

        // ------------------------------------------ Interrupciones y actualizaciones
        new()
        {
            Id = SettingIds.UpdatePaused,
            Group = SettingGroup.Updates,
            Name = "Windows Update en pausa",
            Source = "PauseUpdatesExpiryTime",
            AcceptableValues = new[] { SettingReading.BooleanOn },
            TargetDescription = "Pausado durante la temporada",
            Impact = SettingImpact.High,
            ActionId = ActionIds.WindowsUpdatePause,
            Rationale = "Una actualización a mitad de montaje se lleva por delante drivers de vídeo.",
        },
        new()
        {
            Id = SettingIds.RebootPending,
            Group = SettingGroup.Updates,
            Name = "Reinicio pendiente",
            Source = "RebootRequired + PendingFileRenameOperations + CBS",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Ninguno",
            Impact = SettingImpact.High,
            Rationale = "Un reinicio pendiente es un reinicio que ocurrirá en el peor momento posible.",
        },
        new()
        {
            Id = SettingIds.AutoRebootBlocked,
            Group = SettingGroup.Updates,
            Name = "Reinicio automático bloqueado",
            Source = "NoAutoRebootWithLoggedOnUsers",
            AcceptableValues = new[] { SettingReading.BooleanOn },
            TargetDescription = "Bloqueado",
            Impact = SettingImpact.High,
            Rationale = "Impide que Windows reinicie por su cuenta con sesión iniciada.",
        },
        new()
        {
            Id = SettingIds.DriversViaUpdate,
            Group = SettingGroup.Updates,
            Name = "Drivers vía Windows Update",
            Source = "ExcludeWUDriversInQualityUpdate",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Excluidos",
            Impact = SettingImpact.High,
            Rationale = "Windows sustituye tu driver NVIDIA Studio validado por el que le parece.",
        },
        new()
        {
            Id = SettingIds.DeliveryOptimization,
            Group = SettingGroup.Updates,
            Name = "Delivery Optimization",
            Source = @"...\DeliveryOptimization\DODownloadMode",

            // Lo que importa aquí es que no haya intercambio entre pares, y eso lo cumplen
            // tres modos, no solo el 0: el 99 es descarga simple sin par ni servicio en la
            // nube, y el 100 se salta Delivery Optimization entero y baja por BITS. Con
            // solo el 0 en la lista, una máquina en modo 99 salía en rojo mientras la
            // propia fila decía «sin descarga entre pares», que es la clase de falso rojo
            // que hace que dejes de mirar la pantalla.
            AcceptableValues = new[] { "0", "99", "100" },
            TargetDescription = "Sin descarga entre pares (0, 99 o 100)",
            Impact = SettingImpact.High,
            Rationale = "Con el valor por defecto tu máquina sirve actualizaciones a la red del venue, la misma que lleva Dante o NDI.",
        },
        new()
        {
            Id = SettingIds.FocusAssist,
            Group = SettingGroup.Updates,
            Name = "Asistente de concentración",
            Source = @"HKCU\...\QuietHours",
            AcceptableValues = new[] { "AlarmsOnly", "PriorityOnly" },
            TargetDescription = "Solo alarmas",
            Impact = SettingImpact.High,
            ActionId = ActionIds.FocusAssist,
            Rationale = "Una notificación en pantalla completa sale en la pared LED.",
        },
        new()
        {
            Id = SettingIds.Notifications,
            Group = SettingGroup.Updates,
            Name = "Notificaciones del sistema",
            Source = @"...\PushNotifications\ToastEnabled",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivadas",
            Impact = SettingImpact.High,
            Rationale = "Mismo motivo que el anterior, por otra vía.",
        },
        new()
        {
            Id = SettingIds.StoreAutoUpdate,
            Group = SettingGroup.Updates,
            Name = "Actualizaciones automáticas de la Store",
            Source = @"...\WindowsStore\AutoDownload",
            AcceptableValues = new[] { "2" },
            TargetDescription = "Desactivadas",
            Impact = SettingImpact.Medium,
            Rationale = "Descarga en segundo plano compitiendo por la red.",
        },
        new()
        {
            Id = SettingIds.CoInstallers,
            Group = SettingGroup.Updates,
            Name = "Co-installers de dispositivo",
            Source = @"HKLM\...\Device Installer\DisableCoInstallers",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivados",
            Impact = SettingImpact.High,
            Rationale = "Enchufas una capturadora a mitad de montaje y Windows ejecuta el instalador del fabricante: mete software, reinicia servicios y a veces te cambia el dispositivo de audio por defecto.",
        },
        new()
        {
            Id = SettingIds.SearchIndexing,
            Group = SettingGroup.Updates,
            Name = "Indexación en unidades de datos",
            Source = "Win32_Volume.IndexingEnabled",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Ninguna unidad de datos indexada",
            Impact = SettingImpact.Medium,
            Rationale = "El indexador leyendo una carpeta de material mientras grabas en ese mismo disco.",
        },
        new()
        {
            Id = SettingIds.StorageSense,
            Group = SettingGroup.Updates,
            Name = "Sensor de almacenamiento",
            Source = @"...\StorageSense\AllowStorageSenseGlobal",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.Low,
            Rationale = "Borra por su cuenta en discos donde guardas material.",
        },

        // ------------------------------------------------------------------ Seguridad
        new()
        {
            Id = SettingIds.TamperProtection,
            Group = SettingGroup.Security,
            Name = "Protección contra manipulaciones",
            Source = @"...\Windows Defender\Features\TamperProtection",
            TargetDescription = "Solo lectura",
            Impact = SettingImpact.Low,
            Rationale = "Con esto activo, Defender no se toca por software. Se informa y punto.",
        },
        new()
        {
            Id = SettingIds.RealTimeProtection,
            Group = SettingGroup.Security,
            Name = "Protección en tiempo real",
            Source = "Get-MpComputerStatus",
            TargetDescription = "Solo lectura",
            Impact = SettingImpact.Low,
            Rationale = "La jugada correcta no es apagarlo: son las exclusiones de carpeta.",
        },
        new()
        {
            Id = SettingIds.ControlledFolderAccess,
            Group = SettingGroup.Security,
            Name = "Acceso controlado a carpetas",
            Source = "EnableControlledFolderAccess",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado, o con las apps AV permitidas",
            Impact = SettingImpact.High,
            Rationale = "Bloquea en silencio que Resolume o vMix escriban en Vídeos. La app falla al guardar y no dice por qué.",
        },

        // ------------------------------------------------------------ Arranque y sesión
        new()
        {
            Id = SettingIds.FastStartup,
            Group = SettingGroup.Startup,
            Name = "Inicio rápido",
            Source = "HiberbootEnabled",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            ActionId = ActionIds.FastStartup,
            Rationale = "Con inicio rápido, apagar no reinicia de verdad: los drivers arrastran el estado anterior.",
        },
        new()
        {
            Id = SettingIds.ScreenSaver,
            Group = SettingGroup.Startup,
            Name = "Salvapantallas",
            Source = @"HKCU\Control Panel\Desktop\ScreenSaveActive",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.High,
            Rationale = "No es cosmética: en un rig que proyecta a una pared, el salvapantallas sale en la pared.",
        },
        new()
        {
            Id = SettingIds.InactivityLock,
            Group = SettingGroup.Startup,
            Name = "Bloqueo por inactividad",
            Source = "InactivityTimeoutSecs",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Nunca",
            Impact = SettingImpact.High,
            Rationale = "Igual que el anterior: la pantalla de bloqueo acaba proyectada.",
        },

        new()
        {
            Id = SettingIds.RestartApps,
            Group = SettingGroup.Startup,
            Name = "Relanzar aplicaciones al iniciar sesión",
            Source = @"HKCU\...\Winlogon\RestartApps",
            AcceptableValues = new[] { SettingReading.BooleanOff },
            TargetDescription = "Desactivado",
            Impact = SettingImpact.Medium,
            Rationale = "Tras un reinicio por corte de luz, Windows reabre Resolume o vMix solo, en un estado que tú no has elegido.",
        },

        // --------------------------------------------------------------- Adaptador de red
        new()
        {
            Id = SettingIds.NicOffloads,
            Group = SettingGroup.Network,
            Name = "Descargas y ahorro de energía en la NIC",
            Source = "MSFT_NetAdapterAdvancedPropertySettingData (root\\StandardCimv2)",
            AcceptableValues = new[] { "0" },
            TargetDescription = "Ningún adaptador con offloads activos",
            Impact = SettingImpact.High,
            Rationale = "Large Send Offload, moderación de interrupciones y Energy Efficient Ethernet rompen la temporización de Dante y de NDI, y no aparecen en ninguna pantalla de red normal.",
        },

        // ----------------------------------------------------------------- Diagnóstico
        new()
        {
            Id = SettingIds.UnexpectedShutdowns,
            Group = SettingGroup.Diagnostics,
            Name = "Apagados inesperados (90 días)",
            Source = "Registro de eventos, ID 6008 y 41",
            TargetDescription = "Informativo",
            Impact = SettingImpact.Medium,
            Rationale = "Evidencia documentada de la calidad de la alimentación del venue.",
        },
        new()
        {
            Id = SettingIds.DevicesWithError,
            Group = SettingGroup.Diagnostics,
            Name = "Dispositivos con error",
            Source = "Win32_PnPEntity.ConfigManagerErrorCode",
            TargetDescription = "Informativo",
            Impact = SettingImpact.Medium,
            Rationale = "Detecta hardware caído sin abrir el Administrador de dispositivos.",
        },
        new()
        {
            Id = SettingIds.TimerResolution,
            Group = SettingGroup.Diagnostics,
            Name = "Resolución del temporizador",
            Source = "NtQueryTimerResolution",
            TargetDescription = "Informativo",
            Impact = SettingImpact.Low,
            Rationale = "Salud del planificador bajo carga.",
        },
        new()
        {
            Id = SettingIds.VirtualizationFirmware,
            Group = SettingGroup.Diagnostics,
            Name = "Virtualización habilitada en BIOS",
            Source = "Win32_Processor.VirtualizationFirmwareEnabled",
            TargetDescription = "Informativo",
            Impact = SettingImpact.Low,
            Rationale = "Contexto para entender el estado de VBS.",
        },
    };

    /// <summary>Todas las filas, en el orden en el que se pintan.</summary>
    public static IReadOnlyList<SettingDefinition> All => Definitions;

    /// <summary>Orden de los grupos en la vista: primero lo que más veces está mal.</summary>
    public static IReadOnlyList<SettingGroup> Groups { get; } = new[]
    {
        SettingGroup.Power,
        SettingGroup.Kernel,
        SettingGroup.Updates,
        SettingGroup.Security,
        SettingGroup.Startup,
        SettingGroup.Network,
        SettingGroup.Diagnostics,
    };

    public static SettingDefinition? Find(string id) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Compara las lecturas contra el catálogo. Las filas sin lectura salen como
    /// desconocidas, que no es lo mismo que estar mal: la UI las esconde por defecto en vez
    /// de pintarlas en rojo y hacerte perder el tiempo.
    /// </summary>
    public static SettingsAudit Evaluate(
        IEnumerable<SettingReading> readings, IReadOnlySet<string>? writableIds = null)
    {
        ArgumentNullException.ThrowIfNull(readings);

        Dictionary<string, SettingReading> byId = new(StringComparer.Ordinal);
        foreach (SettingReading reading in readings)
        {
            byId[reading.Id] = reading;
        }

        List<SettingGroupSummary> groups = new();

        foreach (SettingGroup group in Groups)
        {
            List<SettingEvaluation> evaluations = new();

            foreach (SettingDefinition definition in Definitions.Where(d => d.Group == group))
            {
                byId.TryGetValue(definition.Id, out SettingReading? reading);

                evaluations.Add(EvaluateOne(definition, reading) with
                {
                    Writable = writableIds?.Contains(definition.Id) == true,
                });
            }

            if (evaluations.Count > 0)
            {
                groups.Add(new SettingGroupSummary { Group = group, Settings = evaluations });
            }
        }

        return new SettingsAudit { Groups = groups };
    }

    internal static SettingEvaluation EvaluateOne(SettingDefinition definition, SettingReading? reading)
    {
        string? current = reading?.Value;

        SettingVerdict verdict;

        if (!definition.HasTarget)
        {
            // Sin objetivo la fila es informativa aunque no se haya podido leer: marcarla
            // como desconocida sugeriria que falta un dato que importa, y no es el caso.
            verdict = SettingVerdict.Informational;
        }
        else if (current is null)
        {
            verdict = SettingVerdict.Unknown;
        }
        else if (Matches(definition, current))
        {
            verdict = SettingVerdict.Match;
        }
        else
        {
            verdict = definition.Impact == SettingImpact.High
                ? SettingVerdict.DiffersCritical
                : SettingVerdict.Differs;
        }

        return new SettingEvaluation
        {
            Definition = definition,
            Verdict = verdict,
            Current = current,
            Display = reading?.Display,
            Unavailable = reading?.Unavailable,
        };
    }

    private static bool Matches(SettingDefinition definition, string current) =>
        definition.AcceptableValues.Any(v =>
            string.Equals(v, current, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nombre legible del grupo, para las cabeceras.</summary>
    public static string DescribeGroup(SettingGroup group) => group switch
    {
        SettingGroup.Power => "Energía",
        SettingGroup.Network => "Adaptador de red",
        SettingGroup.Kernel => "Kernel y rendimiento",
        SettingGroup.Updates => "Interrupciones y actualizaciones",
        SettingGroup.Security => "Seguridad",
        SettingGroup.Startup => "Arranque y sesión",
        SettingGroup.Diagnostics => "Diagnóstico",
        _ => group.ToString(),
    };

    public static string DescribeImpact(SettingImpact impact) => impact switch
    {
        SettingImpact.High => "Alto",
        SettingImpact.Medium => "Medio",
        SettingImpact.Low => "Bajo",
        _ => impact.ToString(),
    };
}
