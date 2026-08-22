using System.Net;
using AvDashboard.Agent;
using AvDashboard.Agent.Auth;
using AvDashboard.Agent.Collectors;
using AvDashboard.Agent.Endpoints;
using AvDashboard.Agent.Hubs;
using AvDashboard.Agent.Modules;
using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Taxonomy;

// -----------------------------------------------------------------------------
// AV Dashboard — agente
//
// El mismo binario corre como servicio de Windows (LocalSystem, arranca antes del
// login) y en modo portatil desde un USB (sesion de usuario, solo lectura).
// La diferencia esta en privilegios y persistencia, no en el codigo.
// -----------------------------------------------------------------------------

// Ojo con los argumentos: el proveedor de configuracion de linea de comandos exige
// `--clave=valor` y revienta con un `--portable` suelto. Como el agente solo acepta
// interruptores, se parsean a mano y no se le pasan al builder.
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
});

bool portable = args.Contains("--portable", StringComparer.OrdinalIgnoreCase);

builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("AVDASHBOARD_");

AgentOptions options = new();
builder.Configuration.GetSection(AgentOptions.SectionName).Bind(options);
options.PortableMode = options.PortableMode || portable;

// En modo portatil todo vive junto al ejecutable: nada que instalar, nada que dejar
// atras en la maquina de un cliente.
string dataDirectory = options.PortableMode
    ? Path.Combine(AppContext.BaseDirectory, "data")
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AvDashboard");

Directory.CreateDirectory(dataDirectory);

NodeIdentity identity = new(options, dataDirectory);

// En modo portatil, el token se copia ademas a %LocalAppData% para que la consola
// pueda encontrarlo sin privilegios y sin adivinar donde esta el ejecutable.
TokenStore tokens = new(dataDirectory, portable: options.PortableMode);
if (OperatingSystem.IsWindows())
{
    // Rotacion en cada arranque: si alguien copio el token, deja de valer al reiniciar.
    tokens.Rotate();
}

AgentDatabase database = new(Path.Combine(dataDirectory, "avdashboard.db"));
database.Migrate();

ConfigureKestrel(builder, options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(identity);
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<NodeState>();
builder.Services.AddSingleton<EventRepository>();
builder.Services.AddSingleton<PingRepository>();
builder.Services.AddSingleton<ModuleRegistry>();
builder.Services.AddSingleton<ServiceTaxonomy>(_ => new ServiceTaxonomy());

builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase;
    json.SerializerOptions.DefaultIgnoreCondition =
        global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Colectores de la Fase 1. Todos son de solo lectura: es la parte que no puede
// tumbar un evento, y por eso es la primera que existe.
builder.Services.AddHostedService<SystemCollector>();
builder.Services.AddHostedService<NetworkCollector>();
builder.Services.AddHostedService<InventoryCollector>();
builder.Services.AddHostedService<RetentionService>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddHostedService<AudioCollector>();
    builder.Services.AddHostedService<ServiceCollector>();
}

builder.Services.AddHostedService<PingCollector>();

if (!options.PortableMode && OperatingSystem.IsWindows())
{
    // Solo tiene sentido como servicio cuando hay instalacion; en portatil se ejecuta
    // como una aplicacion de consola normal.
    builder.Services.AddWindowsService(service => service.ServiceName = "AvDashboardAgent");
}

WebApplication app = builder.Build();

app.UseMiddleware<BearerTokenMiddleware>();

app.MapAgentApi();
app.MapHub<LiveTelemetryHub>(ApiRoutes.Stream);

ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation(
    "AV Dashboard agente {Version} — nodo {Node} ({Hostname}) — modo {Mode} — datos en {Path}",
    NodeIdentity.AgentVersion, identity.Id, identity.Hostname,
    options.PortableMode ? "portátil" : "servicio", dataDirectory);

if (OperatingSystem.IsWindows())
{
    logger.LogInformation("Token del agente en {TokenPaths}", string.Join(" y ", tokens.WrittenTo));
}

app.Run();

// -----------------------------------------------------------------------------

static void ConfigureKestrel(WebApplicationBuilder builder, AgentOptions options)
{
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // Regla que no se rompe: nunca 0.0.0.0. Loopback para la consola local y, como
        // mucho, la IP de Tailscale para el multi-nodo. Un agente que corre como
        // LocalSystem no se expone a la LAN del hotel.
        if (options.BindLoopback)
        {
            kestrel.Listen(IPAddress.Loopback, options.Port);
        }

        if (options.BindTailscale && NodeIdentity.GetTailscaleAddress() is IPAddress tailscale)
        {
            kestrel.Listen(tailscale, options.Port);
        }

        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
    });
}
