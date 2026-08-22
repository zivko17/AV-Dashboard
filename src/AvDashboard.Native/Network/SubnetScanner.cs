using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;

namespace AvDashboard.Native.Network;

/// <summary>
/// Descubrimiento del segmento (M5): barrido ICMP, resolucion de MAC por ARP y sondeo
/// de puerto web. mDNS y SSDP entran en la Fase 5 con el resto de protocolos AV.
///
/// El barrido es deliberadamente bajo demanda: 250 hosts a la vez no cabe en el
/// presupuesto de CPU de una maquina que esta emitiendo.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SubnetScanner
{
    private readonly OuiDatabase _oui;

    public SubnetScanner(OuiDatabase? ouiDatabase = null)
    {
        _oui = ouiDatabase ?? new OuiDatabase();
    }

    public async Task<ScanResponse> ScanAsync(
        string cidr,
        ScanRequest? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ScanRequest();

        DateTimeOffset started = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();

        IReadOnlyList<IPAddress> targets = EnumerateHosts(cidr);
        List<DiscoveredDevice> found = new();
        object sync = new();

        // 64 en vuelo mantiene el barrido de un /24 por debajo de los 5 s sin dejar la
        // maquina sin sockets ni saturar el conmutador con ARP.
        using SemaphoreSlim gate = new(64);

        IEnumerable<Task> probes = targets.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using Ping ping = new();
                PingReply reply;

                try
                {
                    reply = await ping.SendPingAsync(address, options.TimeoutMs).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is PingException or InvalidOperationException)
                {
                    return;
                }

                string? mac = ResolveMac(address);

                // Un host puede no responder a ICMP y estar ahi: si ARP devuelve MAC,
                // existe. Muchos procesadores LED y switches gestionados hacen esto.
                if (reply.Status != IPStatus.Success && mac is null)
                {
                    return;
                }

                DiscoveryMethod methods = DiscoveryMethod.None;
                if (reply.Status == IPStatus.Success)
                {
                    methods |= DiscoveryMethod.Icmp;
                }

                if (mac is not null)
                {
                    methods |= DiscoveryMethod.Arp;
                }

                int? webPort = null;
                if (options.IncludePortProbe)
                {
                    webPort = await PortProbe
                        .FindWebPortAsync(address, options.TimeoutMs, cancellationToken)
                        .ConfigureAwait(false);

                    if (webPort is not null)
                    {
                        methods |= DiscoveryMethod.TcpProbe;
                    }
                }

                DiscoveredDevice device = new()
                {
                    Id = address.ToString(),
                    IpAddress = address.ToString(),
                    MacAddress = mac,
                    Vendor = _oui.Lookup(mac),
                    SeenBy = methods,
                    WebPort = webPort,
                    OpenPorts = webPort is int p ? new[] { p } : Array.Empty<int>(),
                    FirstSeen = started,
                    LastSeen = DateTimeOffset.UtcNow,
                };

                lock (sync)
                {
                    found.Add(device);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);

        found.Sort(static (a, b) => CompareAddresses(a.IpAddress, b.IpAddress));

        return new ScanResponse
        {
            StartedAt = started,
            Duration = stopwatch.Elapsed,
            Range = cidr,
            Devices = found,
        };
    }

    /// <summary>MAC de un host del segmento via ARP, o null si no esta en la tabla.</summary>
    public static string? ResolveMac(IPAddress address)
    {
        if (address.AddressFamily != global::System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return null;
        }

        // SendARP espera la IPv4 en orden de red, que es justo como sale de GetAddressBytes.
        uint destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);

        byte[] buffer = new byte[6];
        int length = buffer.Length;

        int result = SendARP(destination, 0, buffer, ref length);
        if (result != 0 || length < 6)
        {
            return null;
        }

        return MacAddress.Format(Convert.ToHexString(buffer.AsSpan(0, 6)));
    }

    /// <summary>
    /// Enumera los hosts utiles de un CIDR: se excluyen la direccion de red y la de
    /// broadcast. Rechaza prefijos mas cortos de /16 porque barrer 65.536 direcciones
    /// no es un escaneo, es un ataque de denegacion a tu propio conmutador.
    /// </summary>
    public static IReadOnlyList<IPAddress> EnumerateHosts(string cidr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cidr);

        string[] parts = cidr.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out IPAddress? baseAddress) ||
            !int.TryParse(parts[1], out int prefix))
        {
            throw new FormatException($"«{cidr}» no es un CIDR válido. Formato: 192.168.0.0/24");
        }

        if (baseAddress.AddressFamily != global::System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new NotSupportedException("El barrido solo cubre IPv4.");
        }

        if (prefix is < 16 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr),
                "El prefijo debe estar entre /16 y /32.");
        }

        byte[] bytes = baseAddress.GetAddressBytes();
        Array.Reverse(bytes);
        uint address = BitConverter.ToUInt32(bytes, 0);

        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        uint network = address & mask;
        uint broadcast = network | ~mask;

        if (prefix >= 31)
        {
            // /31 y /32 no tienen direccion de red ni de broadcast reservadas.
            List<IPAddress> single = new();
            for (uint current = network; current <= broadcast; current++)
            {
                single.Add(ToAddress(current));
            }

            return single;
        }

        List<IPAddress> hosts = new((int)(broadcast - network - 1));
        for (uint current = network + 1; current < broadcast; current++)
        {
            hosts.Add(ToAddress(current));
        }

        return hosts;
    }

    /// <summary>CIDR que corresponde a una interfaz, para el boton «escanear este segmento».</summary>
    public static string? CidrFor(NetworkInterfaceInfo nic)
    {
        if (nic.Ipv4Addresses.Count == 0 ||
            !IPAddress.TryParse(nic.Ipv4Addresses[0], out IPAddress? address) ||
            nic.SubnetMask is null ||
            !IPAddress.TryParse(nic.SubnetMask, out IPAddress? mask))
        {
            return null;
        }

        byte[] maskBytes = mask.GetAddressBytes();
        int prefix = 0;
        foreach (byte b in maskBytes)
        {
            prefix += global::System.Numerics.BitOperations.PopCount(b);
        }

        byte[] addressBytes = address.GetAddressBytes();
        for (int i = 0; i < addressBytes.Length; i++)
        {
            addressBytes[i] &= maskBytes[i];
        }

        return $"{new IPAddress(addressBytes)}/{prefix}";
    }

    private static IPAddress ToAddress(uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return new IPAddress(bytes);
    }

    private static int CompareAddresses(string left, string right)
    {
        if (IPAddress.TryParse(left, out IPAddress? a) && IPAddress.TryParse(right, out IPAddress? b))
        {
            byte[] x = a.GetAddressBytes();
            byte[] y = b.GetAddressBytes();

            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                int comparison = x[i].CompareTo(y[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
        }

        return string.CompareOrdinal(left, right);
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] macAddress, ref int physicalAddressLength);
}
