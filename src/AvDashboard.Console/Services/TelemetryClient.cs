using AvDashboard.Core.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AvDashboard.Console.Services;

/// <summary>
/// Cliente del hub de telemetria.
///
/// Se suscribe solo a los canales que la vista activa necesita. Es la mitad de la
/// consola del contrato de coste: el agente no manda medidores a 20 fps si nadie los mira.
/// </summary>
public sealed class TelemetryClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TelemetryClient(AgentEndpoint endpoint)
    {
        Uri hubUri = new(endpoint.BaseAddress, ApiRoutes.Stream);

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri, http =>
            {
                if (endpoint.Token is string token)
                {
                    http.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnected += _ =>
        {
            // Tras una reconexion los grupos del servidor se han perdido: hay que volver
            // a suscribirse o la vista se queda congelada sin decir nada.
            return ResubscribeAsync();
        };
    }

    public HubConnectionState State => _connection.State;

    public event Action<HubConnectionState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != HubConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
            StateChanged?.Invoke(_connection.State);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // El agente no esta arrancado todavia. La consola lo pinta como offline y
            // reintenta; no es una condicion excepcional en esta aplicacion.
            StateChanged?.Invoke(HubConnectionState.Disconnected);
        }
    }

    public IDisposable On<T>(string channel, Action<T> handler) => _connection.On(channel, handler);

    public async Task SubscribeAsync(string channel)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_subscribed.Add(channel))
            {
                return;
            }

            if (_connection.State == HubConnectionState.Connected)
            {
                await _connection.InvokeAsync("Subscribe", channel).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HubException or InvalidOperationException)
        {
            _subscribed.Remove(channel);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnsubscribeAsync(string channel)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_subscribed.Remove(channel))
            {
                return;
            }

            if (_connection.State == HubConnectionState.Connected)
            {
                await _connection.InvokeAsync("Unsubscribe", channel).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HubException or InvalidOperationException)
        {
            // Nada que hacer: al reconectar se recalcula la lista de suscripciones.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResubscribeAsync()
    {
        foreach (string channel in _subscribed.ToArray())
        {
            try
            {
                await _connection.InvokeAsync("Subscribe", channel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HubException or InvalidOperationException)
            {
                // Se reintentara en la siguiente reconexion.
            }
        }

        StateChanged?.Invoke(_connection.State);
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
