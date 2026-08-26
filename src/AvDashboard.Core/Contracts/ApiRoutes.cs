namespace AvDashboard.Core.Contracts;

/// <summary>
/// Rutas de la API del agente, en un solo sitio. El agente las sirve y la consola,
/// el CLI y el puente con Companion las consumen: si cambian, cambian aqui y el
/// compilador senala a todos los que se quedan atras.
/// </summary>
public static class ApiRoutes
{
    public const string Version = "v1";
    public const string Base = "/api/" + Version;

    public const string Node = Base + "/node";
    public const string Modules = Base + "/modules";

    public const string System = Base + "/system";

    /// <summary>Auditoria del nodo contra el catalogo de ajustes con objetivo AV (M1).</summary>
    public const string SystemAudit = Base + "/system/audit";
    public const string SystemSetting = Base + "/system/{setting}";
    public const string Processes = Base + "/system/processes";

    public const string AudioDevices = Base + "/audio/devices";
    public const string AudioDefault = Base + "/audio/default";
    public const string AudioSessions = Base + "/audio/sessions";
    public const string AudioSession = Base + "/audio/session/{pid:int}";
    public const string AudioPtp = Base + "/audio/ptp";
    public const string AudioAsio = Base + "/audio/asio";

    public const string VideoDisplays = Base + "/video/displays";
    public const string VideoDisplaysRestore = Base + "/video/displays/restore";
    public const string VideoWindowsRestore = Base + "/video/windows/restore";
    public const string VideoDeckLink = Base + "/video/decklink";
    public const string VideoSync = Base + "/video/sync";
    public const string VideoLedProcessors = Base + "/video/led-processors";

    /// <summary>Biblioteca de EDID: los conectados y los que Windows recuerda (M18).</summary>
    public const string VideoEdid = Base + "/video/edid";

    /// <summary>Bytes crudos de un EDID, para volcarlos a .bin (`edid-dump`).</summary>
    public const string VideoEdidRaw = Base + "/video/edid/raw";

    /// <summary>Comparacion entre dos EDID (`edid-diff`).</summary>
    public const string VideoEdidDiff = Base + "/video/edid/diff";

    /// <summary>Biblioteca de EDID de referencia y deriva contra ella (M18).</summary>
    public const string VideoEdidReference = Base + "/video/edid/reference";
    public const string VideoGpu = Base + "/video/gpu";

    public const string NetworkInterfaces = Base + "/network/interfaces";
    public const string NetworkProfile = Base + "/network/profile";
    public const string NetworkScan = Base + "/network/scan";

    /// <summary>Inventario de la red: lo visto en los barridos mas lo anadido a mano (M17).</summary>
    public const string NetworkDevices = Base + "/network/devices";

    /// <summary>Un equipo del inventario, por su direccion. Solo para borrarlo.</summary>
    public const string NetworkDevice = Base + "/network/devices/{address}";
    public const string NetworkPing = Base + "/network/ping";

    /// <summary>Muestras en crudo de la ventana en vivo, para el grafico del dashboard.</summary>
    public const string NetworkPingSamples = Base + "/network/ping/samples";
    public const string NetworkArtNet = Base + "/network/protocols/artnet";
    public const string NetworkSacn = Base + "/network/protocols/sacn";
    public const string NetworkNdiDiscovery = Base + "/network/ndi-discovery";

    public const string Services = Base + "/services";
    public const string Service = Base + "/services/{name}";
    public const string ServicesBaseline = Base + "/services/baseline";

    public const string Profiles = Base + "/profiles";
    public const string ProfileApply = Base + "/profiles/{id}/apply";

    /// <summary>Un perfil escrito a fichero, para llevarlo a otra maquina.</summary>
    public const string ProfileExport = Base + "/profiles/export";

    /// <summary>Leer, validar y guardar un perfil que viene de un fichero.</summary>
    public const string ProfileImport = Base + "/profiles/import";
    public const string RunRollback = Base + "/runs/{runId}/rollback";
    public const string Runs = Base + "/runs";

    public const string MediaScan = Base + "/media/scan";
    public const string Licenses = Base + "/licenses";
    public const string PowerUps = Base + "/power/ups";
    public const string Storage = Base + "/storage";
    public const string Panic = Base + "/panic/{action}";
    public const string Manifest = Base + "/manifest";

    public const string Timeline = Base + "/timeline";

    /// <summary>
    /// Devuelve un ajuste al valor que tenia antes de una escritura concreta.
    ///
    /// Se identifica por el evento del Timeline y no por el ajuste: lo que se deshace es
    /// «ese cambio», no «lo ultimo que le paso a esta fila». Si alguien lo toco despues, el
    /// agente lo detecta y se niega.
    /// </summary>
    public const string TimelineUndo = Timeline + "/{eventId:long}/undo";
    public const string Alerts = Base + "/alerts";
    public const string ReportPreShow = Base + "/report/preshow";
    public const string CompanionVariables = Base + "/companion/variables";

    /// <summary>Hub de SignalR para la telemetria en vivo.</summary>
    public const string Stream = Base + "/stream";

    /// <summary>
    /// Toda escritura acepta este parametro y devuelve el diff sin aplicar nada.
    /// No es opcional en el diseno: es la red de seguridad de todo el modulo de perfiles.
    /// </summary>
    public const string DryRunQuery = "dryRun";
}

/// <summary>
/// Nombres de las acciones de panico, tal y como viajan en la URL.
///
/// Se declaran aparte de <see cref="Profiles.ActionIds"/> a proposito: un actuador es algo
/// que un perfil puede aplicar, y estas no lo son nunca.
/// </summary>
public static class PanicActions
{
    public const string RestartGraphicsDriver = "restart-graphics-driver";
}
