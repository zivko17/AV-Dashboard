using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AvDashboard.Agent.Hubs;

/// <summary>
/// Hub de telemetria en vivo.
///
/// La consola se suscribe solo a los canales de la vista que tiene abierta. Mandar
/// medidores de audio a 20 fps a una consola que esta mirando la pestana de Red es
/// gastar CPU en la maquina que estamos intentando optimizar, y el presupuesto es del 1 %.
/// </summary>
public sealed class LiveTelemetryHub : Hub
{
    private static readonly ConcurrentDictionary<string, int> Subscribers = new(StringComparer.Ordinal);

    public async Task Subscribe(string channel)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, channel).ConfigureAwait(false);
        Subscribers.AddOrUpdate(channel, 1, static (_, count) => count + 1);
    }

    public async Task Unsubscribe(string channel)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel).ConfigureAwait(false);
        Subscribers.AddOrUpdate(channel, 0, static (_, count) => Math.Max(0, count - 1));
    }

    /// <summary>
    /// Cuantas consolas escuchan un canal. Los colectores caros lo consultan antes de
    /// trabajar: si nadie mira los medidores, no se leen los medidores.
    /// </summary>
    public static bool HasSubscribers(string channel) =>
        Subscribers.TryGetValue(channel, out int count) && count > 0;

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // No sabemos a que canales estaba suscrito este cliente sin llevar un mapa por
        // conexion; el contador se corrige solo porque SignalR saca la conexion de todos
        // los grupos, y un contador alto de mas solo significa trabajar de mas un rato.
        foreach (string channel in Subscribers.Keys)
        {
            Subscribers.AddOrUpdate(channel, 0, static (_, count) => Math.Max(0, count - 1));
        }

        return base.OnDisconnectedAsync(exception);
    }
}
