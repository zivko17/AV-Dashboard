using System.Net;
using System.Net.NetworkInformation;
using AvDashboard.Core.Models;
using SocketExceptionAlias = System.Net.Sockets.SocketException;

namespace AvDashboard.Native.Network;

/// <summary>
/// Sonda de ping. Envuelve <see cref="Ping"/> para que un host inalcanzable devuelva
/// una muestra perdida en lugar de una excepcion: el ping continuo tiene que sobrevivir
/// a que le quiten el cable, que es precisamente cuando hace falta.
/// </summary>
public sealed class PingProbe : IDisposable
{
    private readonly Ping _ping = new();
    private readonly byte[] _payload = new byte[32];
    private bool _disposed;

    public async Task<PingSample> SendAsync(
        string host,
        int timeoutMs = 1000,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        try
        {
            PingReply reply = await _ping
                .SendPingAsync(host, timeoutMs, _payload)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PingSample
            {
                Timestamp = timestamp,
                RttMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
            };
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketExceptionAlias)
        {
            return new PingSample { Timestamp = timestamp, RttMs = null };
        }
    }

    public async Task<PingSample> SendAsync(
        IPAddress address,
        int timeoutMs = 1000,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        try
        {
            PingReply reply = await _ping
                .SendPingAsync(address, timeoutMs, _payload)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PingSample
            {
                Timestamp = timestamp,
                RttMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
            };
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketExceptionAlias)
        {
            return new PingSample { Timestamp = timestamp, RttMs = null };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ping.Dispose();
        _disposed = true;
    }
}
