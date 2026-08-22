using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AvDashboard.Core.Models;

namespace AvDashboard.Agent;

/// <summary>
/// Identidad del nodo: un id estable entre reinicios, el hostname y las direcciones por
/// las que se le puede alcanzar.
/// </summary>
public sealed class NodeIdentity
{
    private readonly AgentOptions _options;

    public NodeIdentity(AgentOptions options, string dataDirectory)
    {
        _options = options;
        Id = LoadOrCreateId(dataDirectory);
        Hostname = options.NodeName ?? Environment.MachineName;
    }

    public string Id { get; }
    public string Hostname { get; }

    public static string AgentVersion =>
        typeof(NodeIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public NodeInfo Describe(bool isService, string? activeProfile) => new()
    {
        Id = Id,
        Hostname = Hostname,
        LanIp = GetLanAddress()?.ToString(),
        TailscaleIp = GetTailscaleAddress()?.ToString(),
        AgentVersion = AgentVersion,
        OsVersion = Environment.OSVersion.VersionString,
        Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        LastSeen = DateTimeOffset.UtcNow,
        IsService = isService,
        ActiveProfile = activeProfile,
    };

    /// <summary>
    /// Direccion de Tailscale de esta maquina. El rango 100.64.0.0/10 es CGNAT, que es
    /// lo que usa Tailscale para su red virtual.
    /// </summary>
    public static IPAddress? GetTailscaleAddress()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                byte[] bytes = address.Address.GetAddressBytes();
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return address.Address;
                }
            }
        }

        return null;
    }

    /// <summary>Primera IPv4 de una interfaz fisica activa que no sea de Tailscale.</summary>
    public static IPAddress? GetLanAddress()
    {
        IPAddress? tailscale = GetTailscaleAddress();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (tailscale is not null && address.Address.Equals(tailscale))
                {
                    continue;
                }

                return address.Address;
            }
        }

        return null;
    }

    private static string LoadOrCreateId(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "node-id");

        try
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out _))
                {
                    return existing;
                }
            }

            string generated = Guid.NewGuid().ToString("D");
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, generated);
            return generated;
        }
        catch (IOException)
        {
            // Sin disco escribible (USB de solo lectura) el id es efimero y no pasa nada:
            // el modo portatil no participa en la vista consolidada de flota.
            return Guid.NewGuid().ToString("D");
        }
    }
}
