using System.Net.Http.Headers;
using System.Text.Json;
using AvDashboard.Core.Contracts;

namespace AvDashboard.Cli;

/// <summary>
/// Punto de entrada del CLI.
///
/// Sin System.CommandLine: los comandos son una palabra y como mucho un argumento, que
/// es exactamente el diseno que ya funciona en av-light-tools. Anadir un parser de
/// 200 KB para eso seria cambiar simplicidad por ceremonia.
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        string name = args[0];
        string[] rest = args[1..];

        CommandDescriptor? command = CommandCatalog.Find(name);
        if (command is null)
        {
            Console.Error.WriteLine($"Comando desconocido: «{name}». Usa `avdash help` para ver la lista.");
            return 2;
        }

        bool json = rest.Contains("--json", StringComparer.OrdinalIgnoreCase);

        try
        {
            using AgentConnection connection = AgentConnection.Create(ReadPort(rest));
            return await ExecuteAsync(connection, command, rest, json).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"No se ha podido hablar con el agente: {ex.Message}");
            Console.Error.WriteLine("¿Está el servicio AvDashboardAgent arrancado?");
            return 3;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                "No se ha podido leer el token del agente. Ejecuta esto desde un terminal " +
                "elevado, o pásalo con AVDASHBOARD_TOKEN.");
            return 4;
        }
    }

    private static async Task<int> ExecuteAsync(
        AgentConnection connection,
        CommandDescriptor command,
        string[] arguments,
        bool json)
    {
        // Los comandos de escritura llegan con los actuadores de la Fase 2. Hasta
        // entonces el CLI lo dice en vez de fallar con un 501 sin contexto.
        if (command.IsWrite && command.Name is not ("scan" or "preshow"))
        {
            Console.Error.WriteLine(
                $"«{command.Name}» escribe en el sistema y todavía no está implementado " +
                "(llega en la Fase 2, con dry-run y rollback).");
            return 5;
        }

        string payload = command.Method == CommandMethod.Get
            ? await connection.GetAsync(BuildRoute(command, arguments)).ConfigureAwait(false)
            : await connection.PostAsync(BuildRoute(command, arguments)).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(payload);
            return 0;
        }

        Formatter.Print(command.Name, payload);
        return 0;
    }

    private static string BuildRoute(CommandDescriptor command, string[] arguments)
    {
        // `timeline 30` = los ultimos 30 minutos.
        if (command.Name == "timeline" && arguments.Length > 0 && int.TryParse(arguments[0], out int minutes))
        {
            DateTimeOffset to = DateTimeOffset.UtcNow;
            DateTimeOffset from = to.AddMinutes(-minutes);
            return $"{command.Route}?from={Uri.EscapeDataString(from.ToString("O"))}" +
                   $"&to={Uri.EscapeDataString(to.ToString("O"))}";
        }

        return command.Route;
    }

    private static int ReadPort(string[] arguments)
    {
        int index = Array.FindIndex(arguments, a =>
            a.Equals("--port", StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < arguments.Length &&
               int.TryParse(arguments[index + 1], out int port)
            ? port
            : Core.Taxonomy.AvPorts.AgentPort;
    }

    private static bool IsHelp(string argument) =>
        argument is "help" or "--help" or "-h" or "/?";

    private static void PrintHelp()
    {
        Console.WriteLine("avdash — CLI de AV Dashboard");
        Console.WriteLine();
        Console.WriteLine("  Uso: avdash <comando> [argumentos] [--json] [--port N]");
        Console.WriteLine();

        int width = CommandCatalog.All.Max(c => c.Name.Length) + 2;

        foreach (CommandDescriptor command in CommandCatalog.All)
        {
            string aliases = command.Aliases.Count > 0
                ? $"  (también: {string.Join(", ", command.Aliases)})"
                : string.Empty;

            Console.WriteLine($"  {command.Name.PadRight(width)}{command.Summary}{aliases}");
        }

        Console.WriteLine();
        Console.WriteLine("  Los comandos que escriben en el sistema llegan en la Fase 2.");
    }
}
