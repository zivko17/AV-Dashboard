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
    /// El token del agente local esta en %ProgramData%\AvDashboard\agent.token con ACL
    /// de Administradores. Si la consola no corre elevada no lo puede leer, y eso hay que
    /// decirlo con claridad en vez de fallar con un 401 sin explicacion.
    /// </summary>
    public static string? TryReadLocalToken()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AvDashboard", "agent.token");

        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
