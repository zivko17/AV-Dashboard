namespace AvDashboard.Console.Services;

/// <summary>Un agente al que la consola se puede conectar.</summary>
public sealed record AgentEndpoint
{
    public required string Name { get; init; }
    public required Uri BaseAddress { get; init; }

    /// <summary>Token bearer. En localhost se lee del fichero; en remoto se guarda al emparejar.</summary>
    public string? Token { get; init; }

    public bool IsLocal => BaseAddress.IsLoopback;

    public static AgentEndpoint Localhost(int port = 47821) => new()
    {
        Name = "Localhost",
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Token = TryReadLocalToken(),
    };

    /// <summary>
    /// Rutas donde puede estar el token del agente local, en orden de preferencia.
    ///
    /// La variable de entorno gana sobre todo, y es la unica via cuando quieres no dejar
    /// nada en la maquina. Despues va la del modo servicio, con ACL de Administradores;
    /// si la consola no corre elevada no la puede leer, y eso hay que decirlo con
    /// claridad en vez de fallar con un 401 sin explicacion. La ultima es la del modo
    /// portatil, que no necesita privilegios.
    /// </summary>
    public static IReadOnlyList<string> TokenSearchPaths { get; } = new[]
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AvDashboard", "agent.token"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AvDashboard", "agent.token"),
    };

    public static string? TryReadLocalToken()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("AVDASHBOARD_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        foreach (string path in TokenSearchPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    string token = File.ReadAllText(path).Trim();
                    if (token.Length > 0)
                    {
                        return token;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sin permiso para esta: se prueba la siguiente.
            }
        }

        return null;
    }

    /// <summary>Explicacion para la UI cuando no hay token en ninguna parte.</summary>
    public static string DescribeMissingToken() =>
        "No se ha encontrado el token del agente. Se ha buscado en AVDASHBOARD_TOKEN y en:" +
        Environment.NewLine + string.Join(Environment.NewLine, TokenSearchPaths) +
        Environment.NewLine +
        "Si el agente corre como servicio, abre la consola como administrador.";
}
