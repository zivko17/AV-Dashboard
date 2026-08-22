using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;

namespace AvDashboard.Client;

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

    /// <summary>
    /// Cambia un ajuste del catalogo. Con dryRun se calcula el diff sin tocar nada, que es
    /// lo que usara el editor de perfiles antes de aplicar.
    /// </summary>
    public Task<ApiResult<SettingWriteResponse>> ApplySettingAsync(
        string settingId, string value, bool dryRun = false, CancellationToken ct = default) =>
        PostAsync<SettingWriteResponse>(
            ApiRoutes.SystemSetting.Replace("{setting}", Uri.EscapeDataString(settingId),
                StringComparison.Ordinal) +
            $"?value={Uri.EscapeDataString(value)}" + (dryRun ? "&dryRun=true" : string.Empty),
            null, ct);

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

        // El cuerpo se lee primero como texto y solo despues se intenta interpretar. Leerlo
        // directamente como ApiError deja sin nada que ensenar cuando el agente devuelve
        // otra forma: el deserializador lanza por los campos obligatorios que faltan y lo
        // unico que queda es el codigo de estado, que no dice que hacer.
        string body = string.Empty;

        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
        }

        try
        {
            if (JsonSerializer.Deserialize<ApiError>(body, Json) is { Message.Length: > 0 } error)
            {
                return ApiResult<T>.Failed(error.Message);
            }
        }
        catch (JsonException)
        {
            // El agente ha devuelto algo que no es nuestro formato de error.
        }

        string resumen = body.Trim();

        if (resumen.Length > 300)
        {
            resumen = resumen[..300] + "…";
        }

        return ApiResult<T>.Failed(resumen.Length > 0
            ? $"El agente respondió {(int)response.StatusCode}: {resumen}"
            : $"El agente respondió {(int)response.StatusCode}.");
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

/// <summary>Lo que devuelve escribir un ajuste.</summary>
public sealed record SettingWriteResponse
{
    public string Id { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? PreviousValue { get; init; }
    public string? AppliedValue { get; init; }
    public bool DryRun { get; init; }
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
