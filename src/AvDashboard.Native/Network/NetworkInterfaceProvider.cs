using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using Microsoft.Win32;

namespace AvDashboard.Native.Network;

/// <summary>
/// Interfaces de red (M5 base) mas los dos ajustes que no aparecen en ninguna UI de
/// Windows y que tumban Dante y NDI en silencio: el power management del adaptador y
/// Nagle por interfaz.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkInterfaceProvider
{
    private const string NetworkClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private const string TcpipInterfacesKey =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private readonly Dictionary<string, (long Sent, long Received, DateTimeOffset At)> _previous =
        new(StringComparer.Ordinal);

    public IReadOnlyList<NetworkInterfaceInfo> GetInterfaces(bool includeLoopback = false)
    {
        List<NetworkInterfaceInfo> results = new();
        Dictionary<string, RegistryAdapterSettings> registrySettings = ReadRegistrySettings();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!includeLoopback && nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel &&
                !nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
            {
                // Los tuneles del sistema no aportan nada al diagnostico; Tailscale si.
                continue;
            }

            IPInterfaceProperties properties = nic.GetIPProperties();
            IPv4InterfaceProperties? v4 = TryGetIpv4Properties(nic);
            IPInterfaceStatistics statistics = nic.GetIPStatistics();

            List<string> v4Addresses = new();
            List<string> v6Addresses = new();
            string? mask = null;

            foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    v4Addresses.Add(address.Address.ToString());
                    mask ??= address.IPv4Mask?.ToString();
                }
                else if (address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    v6Addresses.Add(address.Address.ToString());
                }
            }

            registrySettings.TryGetValue(nic.Id, out RegistryAdapterSettings settings);

            (double sendMbps, double receiveMbps) = ComputeThroughput(
                nic.Id, statistics.BytesSent, statistics.BytesReceived, now);

            results.Add(new NetworkInterfaceInfo
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                MacAddress = MacAddress.Format(nic.GetPhysicalAddress().ToString()),
                IsUp = nic.OperationalStatus == OperationalStatus.Up,
                IsPhysical = IsPhysicalAdapter(nic.NetworkInterfaceType),
                LinkSpeedBps = nic.Speed,
                Mtu = v4?.Mtu ?? 0,
                IsDhcp = properties.GetIPv4Properties()?.IsDhcpEnabled ?? false,
                Ipv4Addresses = v4Addresses,
                Ipv6Addresses = v6Addresses,
                SubnetMask = mask,
                Gateways = properties.GatewayAddresses.Select(g => g.Address.ToString()).ToList(),
                DnsServers = properties.DnsAddresses.Select(d => d.ToString()).ToList(),
                PowerManagementEnabled = settings.PowerManagementEnabled,
                NagleDisabled = settings.NagleDisabled,
                BytesSent = statistics.BytesSent,
                BytesReceived = statistics.BytesReceived,
                SendMbps = sendMbps,
                ReceiveMbps = receiveMbps,
            });
        }

        return results;
    }

    private (double Send, double Receive) ComputeThroughput(
        string id, long sent, long received, DateTimeOffset now)
    {
        if (!_previous.TryGetValue(id, out (long Sent, long Received, DateTimeOffset At) previous))
        {
            _previous[id] = (sent, received, now);
            return (0, 0);
        }

        double seconds = (now - previous.At).TotalSeconds;
        _previous[id] = (sent, received, now);

        if (seconds <= 0)
        {
            return (0, 0);
        }

        // Los contadores son de 64 bits pero pueden reiniciarse si el adaptador se
        // reinicializa. Un delta negativo es eso, no un throughput negativo.
        double sendDelta = Math.Max(0, sent - previous.Sent);
        double receiveDelta = Math.Max(0, received - previous.Received);

        return (sendDelta * 8 / seconds / 1_000_000, receiveDelta * 8 / seconds / 1_000_000);
    }

    private static IPv4InterfaceProperties? TryGetIpv4Properties(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GetIPv4Properties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recorre las subclaves del adaptador de red en el registro y saca, por GUID de
    /// interfaz, el estado de PnPCapabilities y de Nagle.
    /// </summary>
    private static Dictionary<string, RegistryAdapterSettings> ReadRegistrySettings()
    {
        Dictionary<string, RegistryAdapterSettings> settings = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? classKey = baseKey.OpenSubKey(NetworkClassKey);

            if (classKey is not null)
            {
                foreach (string subKeyName in classKey.GetSubKeyNames())
                {
                    if (!int.TryParse(subKeyName, out _))
                    {
                        continue;
                    }

                    using RegistryKey? adapter = classKey.OpenSubKey(subKeyName);
                    if (adapter?.GetValue("NetCfgInstanceId") is not string instanceId)
                    {
                        continue;
                    }

                    // PnPCapabilities: el bit 0x18 desactiva la gestion de energia.
                    // Ausente significa «Windows decide», que en la practica es activo.
                    int? capabilities = adapter.GetValue("PnPCapabilities") as int?;
                    bool? powerManagement = capabilities is int caps
                        ? (caps & 0x18) != 0x18
                        : null;

                    settings[instanceId] = new RegistryAdapterSettings
                    {
                        PowerManagementEnabled = powerManagement,
                    };
                }
            }

            using RegistryKey? tcpip = baseKey.OpenSubKey(TcpipInterfacesKey);
            if (tcpip is not null)
            {
                foreach (string interfaceGuid in tcpip.GetSubKeyNames())
                {
                    using RegistryKey? interfaceKey = tcpip.OpenSubKey(interfaceGuid);
                    if (interfaceKey is null)
                    {
                        continue;
                    }

                    int? ackFrequency = interfaceKey.GetValue("TcpAckFrequency") as int?;
                    int? noDelay = interfaceKey.GetValue("TCPNoDelay") as int?;

                    bool? nagleDisabled = ackFrequency is null && noDelay is null
                        ? null
                        : ackFrequency == 1 && noDelay == 1;

                    settings.TryGetValue(interfaceGuid, out RegistryAdapterSettings existing);
                    settings[interfaceGuid] = existing with { NagleDisabled = nagleDisabled };
                }
            }
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException)
        {
            // Sin permisos para leer estas ramas: los dos campos quedan en null y la UI
            // dice «no se ha podido leer», que es la verdad.
        }

        return settings;
    }

    /// <summary>Direccion IPv4 principal de la interfaz, util para deducir el rango a escanear.</summary>
    public static IPAddress? GetPrimaryAddress(NetworkInterfaceInfo nic) =>
        nic.Ipv4Addresses.Count > 0 && IPAddress.TryParse(nic.Ipv4Addresses[0], out IPAddress? address)
            ? address
            : null;

    private readonly record struct RegistryAdapterSettings
    {
        public bool? PowerManagementEnabled { get; init; }
        public bool? NagleDisabled { get; init; }
    }

    /// <summary>
    /// Si el tipo corresponde a una tarjeta de red de verdad.
    ///
    /// Lista blanca y no lista negra: Tailscale se declara como propVirtual —ifType 53 de la
    /// IANA, que ni esta en el enum de .NET y sale como un 53 pelado— y no como Tunnel, asi
    /// que descartar los tipos virtuales conocidos lo dejaba pasar. Con una lista negra hay
    /// que ampliarla con cada adaptador virtual nuevo, y eso se descubre siempre tarde.
    /// </summary>
    private static bool IsPhysicalAdapter(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => true,
        NetworkInterfaceType.GigabitEthernet => true,
        NetworkInterfaceType.FastEthernetT => true,
        NetworkInterfaceType.FastEthernetFx => true,
        NetworkInterfaceType.Wireless80211 => true,
        _ => false,
    };

}
