using System.Collections.ObjectModel;

namespace AvDashboard.Client;

/// <summary>
/// Los nodos que conoce la consola y cual esta seleccionado.
///
/// El selector de nodo esta en la barra superior y todo lo que se pinta debajo
/// corresponde al nodo activo. Sin ambiguedad posible: es la regla que hace que el
/// multi-nodo no se convierta en una fuente de errores a las siete de la tarde.
/// </summary>
public sealed class NodeManager : IDisposable
{
    private readonly Dictionary<string, ApiClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TelemetryClient> _telemetry = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public NodeManager()
    {
        Endpoints = new ObservableCollection<AgentEndpoint> { AgentEndpoint.Localhost() };
        Selected = Endpoints[0];
    }

    public ObservableCollection<AgentEndpoint> Endpoints { get; }

    public AgentEndpoint Selected { get; private set; }

    public event Action<AgentEndpoint>? SelectionChanged;

    public void Select(AgentEndpoint endpoint)
    {
        if (ReferenceEquals(endpoint, Selected))
        {
            return;
        }

        Selected = endpoint;
        SelectionChanged?.Invoke(endpoint);
    }

    public ApiClient Api(AgentEndpoint? endpoint = null)
    {
        AgentEndpoint target = endpoint ?? Selected;

        if (!_clients.TryGetValue(target.Name, out ApiClient? client))
        {
            client = new ApiClient(target);
            _clients[target.Name] = client;
        }

        return client;
    }

    public TelemetryClient Telemetry(AgentEndpoint? endpoint = null)
    {
        AgentEndpoint target = endpoint ?? Selected;

        if (!_telemetry.TryGetValue(target.Name, out TelemetryClient? client))
        {
            client = new TelemetryClient(target);
            _telemetry[target.Name] = client;
        }

        return client;
    }

    /// <summary>
    /// Refresca el token del agente local. Se llama cuando una peticion devuelve 401:
    /// el agente rota el token en cada arranque del servicio, asi que un 401 despues de
    /// haber funcionado casi siempre significa «el servicio se ha reiniciado».
    /// </summary>
    public void RefreshLocalToken()
    {
        string? token = AgentEndpoint.TryReadLocalToken();

        for (int i = 0; i < Endpoints.Count; i++)
        {
            if (!Endpoints[i].IsLocal)
            {
                continue;
            }

            AgentEndpoint refreshed = Endpoints[i] with { Token = token };
            bool wasSelected = ReferenceEquals(Endpoints[i], Selected);

            if (_clients.Remove(refreshed.Name, out ApiClient? stale))
            {
                stale.Dispose();
            }

            Endpoints[i] = refreshed;

            if (wasSelected)
            {
                Selected = refreshed;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (ApiClient client in _clients.Values)
        {
            client.Dispose();
        }

        foreach (TelemetryClient client in _telemetry.Values)
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _clients.Clear();
        _telemetry.Clear();
        _disposed = true;
    }
}
