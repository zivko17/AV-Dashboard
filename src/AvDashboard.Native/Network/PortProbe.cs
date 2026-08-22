using System.Net;
using System.Net.Sockets;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Native.Network;

/// <summary>
/// Sondeo de puertos TCP. Hereda `portcheck` y `portscan` de av-light-tools y anade la
/// resolucion del servicio AV conocido que hay detras del puerto.
/// </summary>
public static class PortProbe
{
    /// <summary>true si el puerto acepta conexion dentro del tiempo dado.</summary>
    public static async Task<bool> IsOpenAsync(
        IPAddress address,
        int port,
        int timeoutMs = 600,
        CancellationToken cancellationToken = default)
    {
        using TcpClient client = new();
        using CancellationTokenSource timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(timeoutMs);

        try
        {
            await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sondea una lista de puertos en paralelo y devuelve los abiertos, ordenados.
    /// El paralelismo esta acotado: 250 dispositivos por 5 puertos son 1250 sockets si
    /// no se limita, y eso lo nota la maquina.
    /// </summary>
    public static async Task<IReadOnlyList<int>> ScanAsync(
        IPAddress address,
        IEnumerable<int> ports,
        int timeoutMs = 600,
        int maxConcurrency = 32,
        CancellationToken cancellationToken = default)
    {
        using SemaphoreSlim gate = new(maxConcurrency);
        List<int> open = new();
        object sync = new();

        IEnumerable<Task> probes = ports.Select(async port =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await IsOpenAsync(address, port, timeoutMs, cancellationToken).ConfigureAwait(false))
                {
                    lock (sync)
                    {
                        open.Add(port);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);

        open.Sort();
        return open;
    }

    /// <summary>
    /// Primer puerto web abierto de la lista habitual. Es lo que habilita el boton
    /// «abrir interfaz web» en la tabla de dispositivos.
    /// </summary>
    public static async Task<int?> FindWebPortAsync(
        IPAddress address,
        int timeoutMs = 600,
        CancellationToken cancellationToken = default)
    {
        foreach (int port in AvPorts.CommonWebPorts)
        {
            if (await IsOpenAsync(address, port, timeoutMs, cancellationToken).ConfigureAwait(false))
            {
                return port;
            }
        }

        return null;
    }
}
