using System.Net.Http.Headers;

namespace AvDashboard.Cli;

/// <summary>Conexion al agente local. El token sale del fichero o de la variable de entorno.</summary>
public sealed class AgentConnection : IDisposable
{
    private readonly HttpClient _http;

    private AgentConnection(HttpClient http)
    {
        _http = http;
    }

    public static AgentConnection Create(int port)
    {
        HttpClient http = new()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        string token = ReadToken();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new AgentConnection(http);
    }

    public async Task<string> GetAsync(string route)
    {
        using HttpResponseMessage response = await _http.GetAsync(route).ConfigureAwait(false);
        return await ReadAsync(response).ConfigureAwait(false);
    }

    public async Task<string> PostAsync(string route)
    {
        using HttpResponseMessage response = await _http.PostAsync(route, content: null).ConfigureAwait(false);
        return await ReadAsync(response).ConfigureAwait(false);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"El agente respondió {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        return body;
    }

    /// <summary>
    /// AVDASHBOARD_TOKEN gana sobre el fichero. Es lo que permite usar el CLI desde
    /// Companion sin tener que correrlo elevado.
    /// </summary>
    private static string ReadToken()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("AVDASHBOARD_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AvDashboard", "agent.token");

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException($"No se puede leer {path}.", ex);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose() => _http.Dispose();
}
