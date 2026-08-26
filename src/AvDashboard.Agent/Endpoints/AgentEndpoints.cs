using AvDashboard.Agent.Modules;
using AvDashboard.Agent.Storage;
using AvDashboard.Agent.Video;
using System.Net;
using System.Text.Json;
using AvDashboard.Core.Profiles;
using AvDashboard.Core.Settings;
using AvDashboard.Core.Video;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native;
using AvDashboard.Native.Network;
using AvDashboard.Native.System;
using AvDashboard.Native.Video;

namespace AvDashboard.Agent.Endpoints;

/// <summary>
/// Superficie HTTP del agente.
///
/// Solo lectura en la Fase 1. Los endpoints de escritura estan declarados y responden
/// 501 con el motivo: es mejor que la consola sepa que la ruta existe y aun no hace nada
/// a que reciba un 404 y no sepa si es un fallo de version.
/// </summary>
public static class AgentEndpoints
{
    public static void MapAgentApi(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = NodeIdentity.AgentVersion }));

        MapNode(app);
        MapSystem(app);
        MapAudio(app);
        MapNetwork(app);
        MapServices(app);
        MapStorageAndReports(app);
        MapNotYetImplemented(app);
    }

    private static void MapNode(WebApplication app)
    {
        app.MapGet(ApiRoutes.Node, async (
            NodeState state, NodeIdentity identity, EventRepository events) =>
        {
            IReadOnlyList<Alert> alerts = await events.GetActiveAlertsAsync().ConfigureAwait(false);

            return Results.Ok(new NodeResponse
            {
                Node = identity.Describe(
                    isService: OperatingSystem.IsWindows() && Platform.IsLocalSystem,
                    activeProfile: state.ActiveProfile),
                Health = state.Overall(),
                ActiveAlerts = alerts.Count,
            });
        });

        app.MapGet(ApiRoutes.Modules, (ModuleRegistry modules) => Results.Ok(modules.All));

        app.MapGet(ApiRoutes.Alerts, async (EventRepository events) =>
            Results.Ok(await events.GetActiveAlertsAsync().ConfigureAwait(false)));

        app.MapGet(ApiRoutes.Manifest, (NodeIdentity identity) =>
            Results.Ok(ManifestProvider.Build(identity.Id)));
    }

    private static void MapSystem(WebApplication app)
    {
        // La auditoria se calcula en la llamada y no en el ciclo de telemetria: toca WMI y
        // el registro de eventos, que son lentos, y la respuesta no cambia de un segundo a
        // otro. Pagar eso una vez por segundo se comeria el presupuesto de CPU del agente.
        app.MapGet(ApiRoutes.SystemAudit, () =>
            Results.Ok(SettingsCatalog.Evaluate(SettingsProbe.Read(), SettingsWriter.SupportedIds)));

        // Escritura de un ajuste del catalogo. El actuador tiene lista blanca compilada:
        // un identificador que no este en ella se rechaza aunque la peticion venga bien
        // formada. Ver SettingsWriter.
        app.MapPost(ApiRoutes.SystemSetting, async (
            EventRepository events, string setting, string value, bool? dryRun) =>
        {
            SettingWriteResult result = SettingsWriter.Apply(setting, value, dryRun == true);

            if (!result.Success)
            {
                // El cuerpo de un error tiene que ser ApiError y no el resultado del
                // actuador. Devolviendo el resultado, el cliente intenta leerlo como
                // ApiError, no encuentra los campos obligatorios, el deserializador lanza
                // y el motivo real —«acceso denegado», casi siempre— se pierde: el usuario
                // solo ve «el agente respondio 400», que no le dice que hacer.
                return Results.BadRequest(new ApiError
                {
                    Code = "setting_write_failed",
                    Message = result.Message,
                    Detail = setting,
                });
            }

            if (!result.DryRun)
            {
                // Toda escritura deja rastro con el valor anterior. Es el registro de
                // auditoria del diseno, y ademas es lo unico que permite deshacer.
                //
                // Pero una escritura que deja el valor como estaba no es un cambio, y
                // anotarla como «setting.changed» en ambar llenaba el Timeline de lineas
                // del tipo «Estado maximo del procesador: 100 → 100». El Timeline existe
                // para reconstruir que paso a las 21:04, y cada linea que no es un cambio
                // resta a esa capacidad. Se sigue dejando rastro —la escritura ocurrio—,
                // pero con otro tipo y en informativo, para que se pueda filtrar.
                string name = SettingsCatalog.Find(setting)?.Name ?? setting;

                bool changed = !string.Equals(
                    result.PreviousValue, result.AppliedValue, StringComparison.Ordinal);

                await events.AppendAsync("M1", Sector.SystemEssential,
                    changed ? Severity.Warning : Severity.Info,
                    changed ? "setting.changed" : "setting.unchanged",
                    changed
                        ? $"{name}: {result.PreviousValue ?? "sin valor"} → {result.AppliedValue}"
                        : $"{name}: ya estaba en {result.AppliedValue}",
                    payloadJson: JsonSerializer.Serialize(result)).ConfigureAwait(false);
            }

            return Results.Ok(result);
        });

        app.MapGet(ApiRoutes.System, (NodeState state) =>
        {
            SystemStatus? status = state.System;
            SystemSettings? settings = state.SystemSettings;

            if (status is null || settings is null)
            {
                // El primer ciclo del colector aun no ha corrido. 503 con Retry-After es
                // mas honesto que devolver ceros que parecen datos.
                return Results.Json(new ApiError
                {
                    Code = "warming_up",
                    Message = "El agente todavía no tiene una primera lectura de sistema.",
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new SystemResponse
            {
                Status = status,
                Settings = settings,
            });
        });
    }

    private static void MapAudio(WebApplication app)
    {
        app.MapGet(ApiRoutes.AudioDevices, (NodeState state) => Results.Ok(new AudioResponse
        {
            Devices = state.AudioDevices,
            AsioDrivers = state.AsioDrivers,
            Health = AudioDiagnostics.Evaluate(state.AudioDevices),
            SampleRateMismatches = AudioDiagnostics
                .FindSampleRateMismatches(state.AudioDevices)
                .Select(d => d.FriendlyName)
                .ToList(),
        }));

        app.MapGet(ApiRoutes.AudioAsio, (NodeState state) => Results.Ok(state.AsioDrivers));

        app.MapGet(ApiRoutes.AudioPtp, (ModuleRegistry modules) =>
        {
            ModuleDescriptor? module = modules.Get("M3-PTP");

            return Results.Ok(new PtpStatus
            {
                Available = module?.IsAvailable ?? false,
                UnavailableReason = module?.Reason,
            });
        });
    }

    private static void MapNetwork(WebApplication app)
    {
        app.MapGet(ApiRoutes.NetworkInterfaces, (NodeState state) => Results.Ok(new NetworkResponse
        {
            Interfaces = state.Interfaces,
            Health = state.EvaluateSectors().TryGetValue(Sector.Network, out HealthState health)
                ? health
                : HealthState.Unknown,
        }));

        app.MapGet(ApiRoutes.NetworkPing, (NodeState state) => Results.Ok(state.PingStatistics.Values));

        // Ventana en vivo por dispositivo, o todas de golpe si no se indica cual. Sale de
        // memoria: el dashboard la pide cada pocos segundos y no puede costar una consulta.
        app.MapGet(ApiRoutes.NetworkPingSamples, (NodeState state, string? device) =>
        {
            if (device is null)
            {
                return Results.Ok(state.PingWindows);
            }

            return state.PingWindows.TryGetValue(device, out IReadOnlyList<PingSample>? window)
                ? Results.Ok(window)
                : Results.Ok(Array.Empty<PingSample>());
        });

        MapInventory(app);

        app.MapPost(ApiRoutes.NetworkScan, async (
            ScanRequest request, NodeState state, CancellationToken cancellationToken) =>
        {
            string? cidr = request.Cidr;

            if (cidr is null)
            {
                NetworkInterfaceInfo? nic = request.InterfaceId is string id
                    ? state.Interfaces.FirstOrDefault(i => i.Id == id)
                    : state.Interfaces.FirstOrDefault(i => i.IsUp && i.Ipv4Addresses.Count > 0);

                cidr = nic is null ? null : SubnetScanner.CidrFor(nic);
            }

            if (cidr is null)
            {
                return Results.BadRequest(new ApiError
                {
                    Code = "no_range",
                    Message = "No se ha podido deducir el rango a escanear. Indica un CIDR.",
                });
            }

            SubnetScanner scanner = new();

            try
            {
                return Results.Ok(await scanner.ScanAsync(cidr, request, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or NotSupportedException)
            {
                return Results.BadRequest(new ApiError
                {
                    Code = "bad_range",
                    Message = ex.Message,
                });
            }
        });
    }

    /// <summary>
    /// Inventario de la red (M17) y la lista de equipos vigilados (M5).
    ///
    /// Un solo POST para alta manual, renombrar, marcar y desmarcar, porque las cuatro son
    /// «asi queda este equipo». Tener un unico sitio donde se valida es la misma idea que
    /// el conmutador de SettingsWriter: la validacion no se puede rodear construyendo la
    /// peticion a mano.
    /// </summary>
    private static void MapInventory(WebApplication app)
    {
        app.MapGet(ApiRoutes.NetworkDevices, async (
            DeviceRepository devices, CancellationToken cancellationToken) =>
            Results.Ok(await devices.ListAsync(cancellationToken).ConfigureAwait(false)));

        app.MapPost(ApiRoutes.NetworkDevices, async (
            DeviceUpsertRequest request,
            DeviceRepository devices,
            EventRepository events,
            CancellationToken cancellationToken) =>
        {
            // Solo direcciones, no nombres. El indice unico de la tabla es sobre la IP y el
            // barrido produce IP: un nombre que resuelve a una direccion distinta cada dia
            // rompe la identidad del inventario. Y en la red de un show el DNS o no esta o
            // miente, asi que tampoco se pierde gran cosa.
            if (!IPAddress.TryParse(request.IpAddress, out IPAddress? parsed))
            {
                return Results.BadRequest(new ApiError
                {
                    Code = "bad_address",
                    Message = "Hace falta una dirección IP. Los nombres de host no valen aquí.",
                    Detail = request.IpAddress,
                });
            }

            string address = parsed.ToString();

            DiscoveredDevice? known = await devices
                .FindAsync(address, cancellationToken).ConfigureAwait(false);

            DiscoveredDevice device = known is null
                ? new DiscoveredDevice
                {
                    Id = address,
                    IpAddress = address,
                    Alias = Trim(request.Alias),
                    IsCritical = request.IsCritical ?? true,
                    Kind = request.Kind ?? DeviceKind.Unknown,
                    Notes = Trim(request.Notes),
                    SeenBy = DiscoveryMethod.Manual,
                    FirstSeen = DateTimeOffset.UtcNow,

                    // Sin LastSeen a proposito: darlo de alta no prueba que responda. La
                    // columna «visto» dira «midiendo» hasta que conteste, que es la verdad.
                }
                : known with
                {
                    // Null deja el campo como estaba; cadena vacia lo borra. Asi marcar una
                    // estrella no obliga a reenviar el alias y arriesgarse a perderlo.
                    Alias = request.Alias is null ? known.Alias : Trim(request.Alias),
                    IsCritical = request.IsCritical ?? known.IsCritical,
                    Kind = request.Kind ?? known.Kind,
                    Notes = request.Notes is null ? known.Notes : Trim(request.Notes),
                };

            await devices.SaveAsync(device, cancellationToken).ConfigureAwait(false);

            // Marcar y desmarcar cambia lo que se vigila, y eso se apunta. Renombrar no:
            // el Timeline existe para reconstruir que paso a las 21:04, y un cambio de
            // etiqueta no explica ninguna averia.
            bool wasCritical = known?.IsCritical ?? false;

            if (device.IsCritical != wasCritical)
            {
                await events.AppendAsync("M5", Sector.Network, Severity.Info,
                    device.IsCritical ? "device.watched" : "device.unwatched",
                    device.IsCritical
                        ? $"«{device.DisplayName}» entra en el ping continuo."
                        : $"«{device.DisplayName}» sale del ping continuo.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return Results.Ok(device);
        });

        app.MapDelete(ApiRoutes.NetworkDevice, async (
            string address,
            DeviceRepository devices,
            EventRepository events,
            CancellationToken cancellationToken) =>
        {
            DiscoveredDevice? known = await devices
                .FindAsync(address, cancellationToken).ConfigureAwait(false);

            if (known is null)
            {
                return Results.NotFound(new ApiError
                {
                    Code = "device_not_found",
                    Message = "Ese equipo no está en el inventario.",
                    Detail = address,
                });
            }

            await devices.DeleteAsync(address, cancellationToken).ConfigureAwait(false);

            // Borrar un equipo que estaba en rojo dejaria su alerta colgando para siempre:
            // ya no hay nadie que la baje, porque el colector solo limpia las de los
            // equipos que sigue vigilando. El colector tambien lo hace al detectar la baja,
            // pero hacerlo aqui es lo que garantiza que este limpia antes de responder.
            await events.ClearAlertAsync("M5", $"ping.critical:{address}", cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new { removed = address });
        });
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void MapServices(WebApplication app)
    {
        app.MapGet(ApiRoutes.Services, (NodeState state) =>
        {
            List<ServiceSectorGroup> groups = SectorNames.DisplayOrder
                .Select(sector => new ServiceSectorGroup
                {
                    Sector = sector,
                    DisplayName = SectorNames.Display(sector),
                    Services = state.Services
                        .Where(s => s.Sector == sector)
                        .OrderBy(s => s.DisplayName, StringComparer.CurrentCulture)
                        .ToList(),
                })
                .Where(g => g.Services.Count > 0)
                .ToList();

            return Results.Ok(new ServicesResponse
            {
                Sectors = groups,
                Health = state.Services.Any(s => s.IsUnexpectedlyStopped)
                    ? HealthState.Critical
                    : state.Services.Count == 0 ? HealthState.Unknown : HealthState.Ok,
                BaselineTakenAt = state.ServiceBaselineTakenAt,
            });
        });

        app.MapGet(ApiRoutes.ServicesBaseline, (NodeState state) =>
        {
            if (state.ServiceBaseline.Count == 0)
            {
                return Results.Ok(Array.Empty<ServiceDiffEntry>());
            }

            return Results.Ok(WindowsServiceReader.Diff(state.ServiceBaseline, state.Services));
        });

        app.MapPost(ApiRoutes.ServicesBaseline, (NodeState state) =>
        {
            // Tomar baseline es escritura, pero sobre el estado del agente, no sobre la
            // maquina: no toca nada del sistema y por eso si esta en la Fase 1.
            state.ServiceBaseline = state.Services;
            state.ServiceBaselineTakenAt = DateTimeOffset.UtcNow;

            return Results.Ok(new { taken = state.ServiceBaselineTakenAt, count = state.Services.Count });
        });
    }

    private static void MapStorageAndReports(WebApplication app)
    {
        app.MapGet(ApiRoutes.Storage, (NodeState state) => Results.Ok(state.Volumes));

        app.MapGet(ApiRoutes.VideoDisplays, (NodeState state) =>
            Results.Ok(state.Displays ?? new DisplayTopology()));

        MapEdid(app);
        MapProfiles(app);
        MapPanic(app);

        app.MapGet(ApiRoutes.Timeline, async (
            EventRepository events,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? modules,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            DateTimeOffset start = from ?? DateTimeOffset.UtcNow.AddHours(-2);
            DateTimeOffset end = to ?? DateTimeOffset.UtcNow;

            string[]? moduleFilter = string.IsNullOrWhiteSpace(modules)
                ? null
                : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int cap = Math.Clamp(limit ?? 2000, 1, 10000);

            IReadOnlyList<TimelineEvent> found = await events
                .QueryAsync(start, end, moduleFilter, limit: cap, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new TimelineResponse
            {
                Events = found,
                From = start,
                To = end,
                Truncated = found.Count >= cap,
            });
        });

        app.MapPost(ApiRoutes.ReportPreShow, (NodeState state, NodeIdentity identity) =>
            Results.Ok(PreShowChecklist.Build(
                identity.Id,
                state.System,
                state.SystemSettings,
                state.AudioDevices,
                state.Interfaces,
                state.Services,
                state.PingStatistics.Values.ToList(),
                state.Volumes,
                state.Power)));
    }

    /// <summary>
    /// Acciones de pánico (M15).
    ///
    /// La única que existe hoy es el reinicio del controlador gráfico, que no depende de
    /// los actuadores de la Fase 2 porque no cambia ninguna configuración: reinicia un
    /// dispositivo y lo deja como estaba. El resto siguen devolviendo 501 con su motivo.
    ///
    /// Es POST y no GET aunque no reciba cuerpo, porque no es idempotente ni inocuo: cada
    /// llamada deja las salidas en negro un par de segundos.
    /// </summary>
    private static void MapPanic(WebApplication app)
    {
        app.MapPost(ApiRoutes.Panic, async (EventRepository events, string action) =>
        {
            if (!string.Equals(action, PanicActions.RestartGraphicsDriver, StringComparison.Ordinal))
            {
                return Results.Json(
                    ApiError.NotImplemented($"La acción de pánico «{action}» (Fase 2)"),
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            GraphicsRestartResult result = GraphicsDriverRestarter.Restart();

            // Una acción de pánico siempre deja rastro, salga bien o mal. Es exactamente el
            // tipo de cosa que quieres ver en el Timeline al reconstruir qué pasó.
            await events.AppendAsync("M15", Sector.Video,
                result.Success ? Severity.Warning : Severity.Error,
                "panic.restart-graphics-driver",
                result.Message).ConfigureAwait(false);

            return result.Success
                ? Results.Ok(result)
                : Results.Json(result, statusCode: StatusCodes.Status500InternalServerError);
        });
    }

    /// <summary>
    /// Perfiles (M10). Aplicarlos sigue siendo Fase 2; leerlos, exportarlos a fichero e
    /// importarlos no depende de los actuadores y resuelve el caso de llegar a un rig nuevo
    /// sin tener que reescribir el perfil a mano.
    /// </summary>
    private static void MapProfiles(WebApplication app)
    {
        app.MapGet(ApiRoutes.Profiles, async (ProfileRepository profiles) =>
            Results.Ok(new ProfilesResponse
            {
                BuiltIn = ProfileLibrary.All,
                Stored = await profiles.ListAsync().ConfigureAwait(false),
            }));

        app.MapGet(ApiRoutes.ProfileExport, async (
            ProfileRepository profiles, NodeIdentity identity, string id) =>
        {
            ProfileDefinition? profile = await profiles.FindAsync(id).ConfigureAwait(false);

            if (profile is null)
            {
                return Results.NotFound(new ApiError
                {
                    Code = "profile_not_found",
                    Message = "No hay ningún perfil con ese identificador.",
                });
            }

            // Se devuelve como texto y no como objeto a proposito: lo que sale por aqui es
            // exactamente el contenido del fichero, byte a byte, sin que la serializacion
            // de la API lo reescriba con otras mayusculas o otro sangrado.
            return Results.Text(
                ProfileSerializer.Export(profile, identity.Hostname),
                "application/json");
        });

        app.MapPost(ApiRoutes.ProfileImport, async (
            ProfileRepository profiles, HttpRequest request) =>
        {
            using StreamReader reader = new(request.Body);
            string json = await reader.ReadToEndAsync().ConfigureAwait(false);

            ProfileImportResult parsed = ProfileSerializer.Import(json);

            if (!parsed.Success)
            {
                return Results.BadRequest(new ProfileImportResponse
                {
                    Saved = false,
                    Error = parsed.Error,
                });
            }

            ProfileDefinition profile = parsed.Profile!;
            await profiles.SaveAsync(profile).ConfigureAwait(false);

            return Results.Ok(new ProfileImportResponse
            {
                Saved = true,
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                UnknownActions = parsed.UnknownActions,
            });
        });
    }

    /// <summary>
    /// Biblioteca de EDID (M18): `edid-list`, `edid-dump` y `edid-diff`.
    ///
    /// El diff va en GET y no en POST porque no cambia nada: es una consulta con dos
    /// parametros, y asi se puede pegar la URL en un navegador cuando estas depurando.
    /// </summary>
    private static void MapEdid(WebApplication app)
    {
        app.MapGet(ApiRoutes.VideoEdid, (EdidLibrary library) => Results.Ok(library.List()));

        app.MapGet(ApiRoutes.VideoEdidRaw, (EdidLibrary library, string id) =>
        {
            AvDashboard.Native.Video.RawEdid? raw = library.Get(id);

            if (raw is null)
            {
                return Results.NotFound(new ApiError
                {
                    Code = "edid_not_found",
                    Message = "No hay ningún EDID con ese identificador.",
                });
            }

            EdidParser.TryParse(raw.Bytes, out Edid? parsed, out _);

            return Results.Ok(new EdidRawResponse
            {
                Id = id,
                Base64 = Convert.ToBase64String(raw.Bytes),
                ByteCount = raw.Bytes.Length,
                SuggestedFileName = SuggestEdidFileName(parsed, raw.HardwareId),
            });
        });

        app.MapGet(ApiRoutes.VideoEdidReference, async (
            EdidReferenceRepository references, EdidDriftDetector detector, NodeState state) =>
            Results.Ok(new EdidReferenceListResponse
            {
                References = await references.ListAsync().ConfigureAwait(false),
                Drift = await detector.DetectAsync(state.Displays ?? new DisplayTopology())
                    .ConfigureAwait(false),
            }));

        app.MapPost(ApiRoutes.VideoEdidReference, async (
            EdidLibrary library, EdidReferenceRepository references,
            NodeState state, SaveEdidReferenceRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ApiError
                {
                    Code = "name_required",
                    Message = "La referencia necesita un nombre con el que la reconozcas.",
                });
            }

            AvDashboard.Native.Video.RawEdid? raw = library.Get(request.Id);

            if (raw is null)
            {
                return Results.NotFound(new ApiError
                {
                    Code = "edid_not_found",
                    Message = "No hay ningún EDID con ese identificador.",
                });
            }

            string key = EdidConnectorKey.FromInstanceId(raw.InstanceId);

            if (key.Length == 0)
            {
                return Results.BadRequest(new ApiError
                {
                    Code = "connector_unknown",
                    Message = "No se ha podido determinar el conector de esa entrada.",
                });
            }

            // El nombre GDI se guarda solo como pista de donde estaba el dia que se guardo.
            string? deviceName = state.Displays?.Outputs
                .FirstOrDefault(o => EdidConnectorKey.FromDevicePath(o.MonitorDevicePath) == key)
                ?.DeviceName;

            await references.SaveAsync(key, request.Name.Trim(), raw.Fingerprint, raw.Bytes, deviceName)
                .ConfigureAwait(false);

            return Results.Ok(new EdidReference
            {
                ConnectorKey = key,
                Name = request.Name.Trim(),
                Fingerprint = raw.Fingerprint,
                ByteCount = raw.Bytes.Length,
                DeviceName = deviceName,
            });
        });

        app.MapGet(ApiRoutes.VideoEdidDiff, (EdidLibrary library, string left, string right) =>
        {
            Edid? a = library.GetParsed(left);
            Edid? b = library.GetParsed(right);

            if (a is null || b is null)
            {
                return Results.NotFound(new ApiError
                {
                    Code = "edid_not_found",
                    Message = a is null
                        ? "No se ha podido leer el EDID de la izquierda."
                        : "No se ha podido leer el EDID de la derecha.",
                });
            }

            return Results.Ok(new EdidDiffResponse
            {
                LeftId = left,
                RightId = right,
                LeftLabel = a.DisplayLabel,
                RightLabel = b.DisplayLabel,
                Differences = EdidComparer.Compare(a, b),
            });
        });
    }

    /// <summary>
    /// Nombre de fichero con el modelo dentro, para que un cajon lleno de volcados siga
    /// siendo legible dentro de seis meses.
    /// </summary>
    private static string SuggestEdidFileName(Edid? parsed, string fallback)
    {
        string raw = parsed?.MonitorName is { Length: > 0 } name
            ? $"{parsed.ManufacturerId}-{name}"
            : fallback;

        Span<char> cleaned = stackalloc char[raw.Length];
        int written = 0;

        foreach (char c in raw)
        {
            cleaned[written++] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-';
        }

        return new string(cleaned[..written]).Trim('-') + ".bin";
    }

    /// <summary>
    /// Rutas declaradas del diseno que todavia no existen. Responden 501 con el motivo y
    /// la fase en la que llegan, para que la consola pueda pintarlas en gris con una
    /// explicacion en lugar de fallar con un 404 ambiguo.
    /// </summary>
    private static void MapNotYetImplemented(WebApplication app)
    {
        (string Route, string What)[] pending =
        {
            (ApiRoutes.AudioDefault, "Cambiar el dispositivo de audio por defecto (Fase 2)"),
            (ApiRoutes.NetworkProfile, "Aplicar un perfil de IP (Fase 2)"),
            (ApiRoutes.Service, "Controlar servicios (Fase 2)"),
            (ApiRoutes.ProfileApply, "Aplicar un perfil de escenario (Fase 2)"),
            (ApiRoutes.RunRollback, "El rollback de una ejecución (Fase 2)"),
            (ApiRoutes.VideoDisplaysRestore, "Restaurar el layout de pantallas (Fase 4)"),
            (ApiRoutes.VideoWindowsRestore, "Restaurar posiciones de ventanas (Fase 4)"),
            (ApiRoutes.MediaScan, "El validador de contenido (Fase 5)"),
            (ApiRoutes.NetworkNdiDiscovery, "El conmutador de NDI Discovery (Fase 5)"),
        };

        foreach ((string route, string what) in pending)
        {
            app.MapPost(route, () => Results.Json(
                ApiError.NotImplemented(what), statusCode: StatusCodes.Status501NotImplemented));
        }

        (string Route, string What)[] pendingReads =
        {
            (ApiRoutes.VideoDeckLink, "La lectura de DeckLink (Fase 4)"),
            (ApiRoutes.VideoSync, "La desviación de refresh (Fase 4)"),
            (ApiRoutes.VideoLedProcessors, "Los procesadores LED (Fase 7)"),
            (ApiRoutes.NetworkArtNet, "El sniffer de Art-Net (Fase 5)"),
            (ApiRoutes.NetworkSacn, "El sniffer de sACN (Fase 5)"),
            (ApiRoutes.Licenses, "El estado de licencias y dongles (Fase 5)"),
            (ApiRoutes.PowerUps, "El estado del SAI (Fase 5)"),
            (ApiRoutes.CompanionVariables, "El puente con Companion (Fase 6)"),
        };

        foreach ((string route, string what) in pendingReads)
        {
            app.MapGet(route, () => Results.Json(
                ApiError.NotImplemented(what), statusCode: StatusCodes.Status501NotImplemented));
        }
    }
}
