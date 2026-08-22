using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;

namespace AvDashboard.Console.Services;

/// <summary>
/// Cliente de la API del agente.
///
/// Todos los metodos devuelven <see cref="ApiResult{T}"/> en lugar de lanzar: en esta
/// aplicacion «el agente no responde» es un estado normal que hay que pintar, no una
/// excepcion que haya que capturar en cada vista.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // El agente serializa los enums como cadena. Este convertidor lee las dos formas,
        // asi que una consola nueva sigue hablando con un agente viejo.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;
    private bool _disposed;

    public ApiClient(AgentEndpoint endpoint)
    {
        Endpoint = endpoint;

        _http = new HttpClient
        {
            BaseAddress = endpoint.BaseAddress,
            Timeout = TimeSpan.FromSeconds(10),
        };

        if (endpoint.Token is string token)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public AgentEndpoint Endpoint { get; }

    public Task<ApiResult<NodeResponse>> GetNodeAsync(CancellationToken ct = default) =>
        GetAsync<NodeResponse>(ApiRoutes.Node, ct);

    public Task<ApiResult<SystemResponse>> GetSystemAsync(CancellationToken ct = default) =>
        GetAsync<SystemResponse>(ApiRoutes.System, ct);

    public Task<ApiResult<AudioResponse>> GetAudioAsync(CancellationToken ct = default) =>
        GetAsync<AudioResponse>(ApiRoutes.AudioDevices, ct);

    public Task<ApiResult<NetworkResponse>> GetNetworkAsync(CancellationToken ct = default) =>
        GetAsync<NetworkResponse>(ApiRoutes.NetworkInterfaces, ct);

    public Task<ApiResult<ServicesResponse>> GetServicesAsync(CancellationToken ct = default) =>
        GetAsync<ServicesResponse>(ApiRoutes.Services, ct);

    public Task<ApiResult<ProfilesResponse>> GetProfilesAsync(CancellationToken ct = default) =>
        GetAsync<ProfilesResponse>(ApiRoutes.Profiles, ct);

    /// <summary>
    /// La exportacion llega como texto y no como objeto: lo que devuelve la ruta es
    /// exactamente el contenido del fichero, y volver a serializarlo lo cambiaria.
    /// </summary>
    public async Task<ApiResult<string>> ExportProfileAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync($"{ApiRoutes.ProfileExport}?id={Uri.EscapeDataString(id)}", ct)
                .ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? ApiResult<string>.Ok(body)
                : ApiResult<string>.Failed($"El agente respondió {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<string>.Offline(Describe(ex));
        }
    }

    public async Task<ApiResult<ProfileImportResponse>> ImportProfileAsync(
        string json, CancellationToken ct = default)
    {
        try
        {
            using StringContent content = new(json, global::System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http
                .PostAsync(ApiRoutes.ProfileImport, content, ct).ConfigureAwait(false);

            return await ReadAsync<ProfileImportResponse>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<ProfileImportResponse>.Offline(Describe(ex));
        }
    }

    /// <summary>Reinicia el controlador grafico (M15). Deja las salidas en negro un instante.</summary>
    public Task<ApiResult<GraphicsRestartResponse>> RestartGraphicsDriverAsync(
        CancellationToken ct = default) =>
        PostAsync<GraphicsRestartResponse>(
            ApiRoutes.Panic.Replace("{action}", PanicActions.RestartGraphicsDriver,
                StringComparison.Ordinal),
            null, ct);

    public Task<ApiResult<SettingsAudit>> GetSystemAuditAsync(CancellationToken ct = default) =>
        GetAsync<SettingsAudit>(ApiRoutes.SystemAudit, ct);

    public Task<ApiResult<DisplayTopology>> GetDisplaysAsync(CancellationToken ct = default) =>
        GetAsync<DisplayTopology>(ApiRoutes.VideoDisplays, ct);

    public Task<ApiResult<EdidListResponse>> GetEdidAsync(CancellationToken ct = default) =>
        GetAsync<EdidListResponse>(ApiRoutes.VideoEdid, ct);

    public Task<ApiResult<EdidRawResponse>> GetEdidRawAsync(string id, CancellationToken ct = default) =>
        GetAsync<EdidRawResponse>($"{ApiRoutes.VideoEdidRaw}?id={Uri.EscapeDataString(id)}", ct);

    public Task<ApiResult<EdidReferenceListResponse>> GetEdidReferencesAsync(
        CancellationToken ct = default) =>
        GetAsync<EdidReferenceListResponse>(ApiRoutes.VideoEdidReference, ct);

    public Task<ApiResult<EdidReference>> SaveEdidReferenceAsync(
        SaveEdidReferenceRequest request, CancellationToken ct = default) =>
        PostAsync<EdidReference>(ApiRoutes.VideoEdidReference, request, ct);

    public Task<ApiResult<EdidDiffResponse>> GetEdidDiffAsync(
        string left, string right, CancellationToken ct = default) =>
        GetAsync<EdidDiffResponse>(
            $"{ApiRoutes.VideoEdidDiff}?left={Uri.EscapeDataString(left)}&right={Uri.EscapeDataString(right)}",
            ct);

    public Task<ApiResult<IReadOnlyList<PingStatistics>>> GetPingAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<PingStatistics>>(ApiRoutes.NetworkPing, ct);

    public Task<ApiResult<IReadOnlyDictionary<string, IReadOnlyList<PingSample>>>> GetPingWindowsAsync(
        CancellationToken ct = default) =>
        GetAsync<IReadOnlyDictionary<string, IReadOnlyList<PingSample>>>(ApiRoutes.NetworkPingSamples, ct);

    public Task<ApiResult<IReadOnlyList<Alert>>> GetAlertsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<Alert>>(ApiRoutes.Alerts, ct);

    public Task<ApiResult<IReadOnlyList<StorageVolume>>> GetStorageAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<StorageVolume>>(ApiRoutes.Storage, ct);

    public Task<ApiResult<IReadOnlyList<ModuleDescriptor>>> GetModulesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ModuleDescriptor>>(ApiRoutes.Modules, ct);

    public Task<ApiResult<TimelineResponse>> GetTimelineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        GetAsync<TimelineResponse>(
            $"{ApiRoutes.Timeline}?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            ct);

    public Task<ApiResult<PreShowVerdict>> RunPreShowAsync(CancellationToken ct = default) =>
        PostAsync<PreShowVerdict>(ApiRoutes.ReportPreShow, null, ct);

    public Task<ApiResult<ScanResponse>> ScanAsync(ScanRequest request, CancellationToken ct = default) =>
        PostAsync<ScanResponse>(ApiRoutes.NetworkScan, request, ct);

    public Task<ApiResult<IReadOnlyList<ServiceDiffEntry>>> GetServiceDiffAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ServiceDiffEntry>>(ApiRoutes.ServicesBaseline, ct);

    public Task<ApiResult<ServiceBaselineResult>> TakeServiceBaselineAsync(CancellationToken ct = default) =>
        PostAsync<ServiceBaselineResult>(ApiRoutes.ServicesBaseline, null, ct);

    private async Task<ApiResult<T>> GetAsync<T>(string route, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(route, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Offline(Describe(ex));
        }
    }

    private async Task<ApiResult<T>> PostAsync<T>(string route, object? body, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = body is null
                ? await _http.PostAsync(route, content: null, ct).ConfigureAwait(false)
                : await _http.PostAsJsonAsync(route, body, Json, ct).ConfigureAwait(false);

            return await ReadAsync<T>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Offline(Describe(ex));
        }
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            T? value = await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
            return value is null
                ? ApiResult<T>.Failed("El agente devolvió una respuesta vacía.")
                : ApiResult<T>.Ok(value);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return ApiResult<T>.Failed(
                "El agente ha rechazado el token. Si la consola no corre como administrador " +
                "no puede leer %ProgramData%\\AvDashboard\\agent.token.");
        }

        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // El agente ha devuelto algo que no es nuestro formato de error.
        }

        return ApiResult<T>.Failed(error?.Message ?? $"El agente respondió {(int)response.StatusCode}.");
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "El agente no ha respondido a tiempo.",
        HttpRequestException http when http.InnerException is not null => http.InnerException.Message,
        _ => ex.Message,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _http.Dispose();
        _disposed = true;
    }
}

/// <summary>Lo que devuelve el reinicio del controlador grafico.</summary>
public sealed record GraphicsRestartResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Adapters { get; init; } = Array.Empty<string>();
}

/// <summary>Respuesta de tomar un baseline de servicios.</summary>
public sealed record ServiceBaselineResult
{
    public DateTimeOffset? Taken { get; init; }
    public int Count { get; init; }
}

/// <summary>Resultado de una llamada: valor, o el motivo por el que no lo hay.</summary>
public sealed record ApiResult<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsOffline { get; init; }

    public bool Success => Error is null && Value is not null;

    public static ApiResult<T> Ok(T value) => new() { Value = value };
    public static ApiResult<T> Failed(string error) => new() { Error = error };
    public static ApiResult<T> Offline(string error) => new() { Error = error, IsOffline = true };
}
