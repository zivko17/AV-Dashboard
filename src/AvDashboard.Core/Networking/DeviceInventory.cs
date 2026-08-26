using System.Net;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Networking;

/// <summary>
/// Fusion de un barrido de red contra el inventario que ya se tenia.
///
/// Vive en Core y no en el repositorio a proposito. Escrito como un UPDATE a mano en
/// SQLite, «el escaneo no pisa tu alias» seria una promesa que nadie puede comprobar y que
/// se rompe la primera vez que alguien anada una columna al UPDATE. Aqui es una funcion
/// pura con sus pruebas, y el CI la corre tambien en Linux.
/// </summary>
public static class DeviceInventory
{
    /// <summary>
    /// Mezcla lo encontrado con lo que ya habia y dice cuales son nuevos.
    ///
    /// La clave es la IP, que es lo que respeta el indice unico de la tabla y ademas lo
    /// honesto: se hace ping a una direccion, no a una MAC.
    /// </summary>
    public static DeviceMerge Merge(
        IReadOnlyCollection<DiscoveredDevice> inventory,
        IReadOnlyCollection<DiscoveredDevice> scanned,
        DateTimeOffset now)
    {
        Dictionary<string, DiscoveredDevice> byAddress =
            inventory.ToDictionary(d => d.IpAddress, StringComparer.OrdinalIgnoreCase);

        List<DiscoveredDevice> added = new();

        foreach (DiscoveredDevice found in scanned)
        {
            if (byAddress.TryGetValue(found.IpAddress, out DiscoveredDevice? known))
            {
                byAddress[found.IpAddress] = Refresh(known, found, now);
                continue;
            }

            DiscoveredDevice fresh = found with
            {
                FirstSeen = now,
                LastSeen = now,

                // Un barrido nunca marca nada como critico: eso lo decides tu. Si lo
                // hiciera, la primera vez que escaneas la red de un hotel te encontrarias
                // haciendo ping a cuarenta televisores.
                IsCritical = false,
            };

            byAddress[found.IpAddress] = fresh;
            added.Add(fresh);
        }

        return new DeviceMerge
        {
            Devices = Sort(byAddress.Values),
            Added = added,
        };
    }

    /// <summary>
    /// Lo que el barrido puede cambiar de un equipo que ya conociamos, que es poco.
    ///
    /// Alias, estrella, notas y clasificacion son tuyos y no se tocan. Y un campo que el
    /// barrido no ha resuelto —la MAC de un equipo que esta detras de un router, por
    /// ejemplo— no borra el que ya teniamos: no saberlo hoy no significa que ayer fuese
    /// mentira.
    /// </summary>
    private static DiscoveredDevice Refresh(
        DiscoveredDevice known, DiscoveredDevice found, DateTimeOffset now) => known with
        {
            MacAddress = found.MacAddress ?? known.MacAddress,
            Vendor = found.Vendor ?? known.Vendor,
            Hostname = found.Hostname ?? known.Hostname,
            WebPort = found.WebPort ?? known.WebPort,
            OpenPorts = found.OpenPorts.Count > 0 ? found.OpenPorts : known.OpenPorts,
            MdnsServices = found.MdnsServices.Count > 0 ? found.MdnsServices : known.MdnsServices,

            // Las vias de descubrimiento se acumulan: que hoy solo haya contestado al ARP
            // no borra que ayer anunciara un servicio por mDNS.
            SeenBy = known.SeenBy | found.SeenBy,

            LastSeen = now,
        };

    /// <summary>
    /// Criticos primero y despues por direccion, pero ordenando la IP como numero.
    ///
    /// Como texto, .10 va antes que .9 y la lista parece rota. Es un detalle tonto que
    /// hace que una pantalla se lea como hecha o como sin terminar.
    /// </summary>
    public static IReadOnlyList<DiscoveredDevice> Sort(IEnumerable<DiscoveredDevice> devices) =>
        devices
            .OrderByDescending(d => d.IsCritical)
            .ThenBy(d => d.IpAddress, IpAddressOrder.Instance)
            .ToList();

    /// <summary>
    /// Que probes hay que crear y cuales retirar entre dos vueltas del colector.
    ///
    /// Es una diferencia de conjuntos de tres lineas, y esta aqui por lo que cuesta
    /// olvidarla: al retirar un equipo hay que soltar su probe, su ventana de muestras, su
    /// entrada en NodeState y su alerta activa. Un equipo borrado mientras estaba en rojo
    /// deja, si no, una alerta permanente de algo que ya no existe.
    /// </summary>
    public static CriticalSetChange Diff(
        IReadOnlyCollection<string> previous,
        IReadOnlyCollection<string> current)
    {
        HashSet<string> before = new(previous, StringComparer.OrdinalIgnoreCase);
        HashSet<string> after = new(current, StringComparer.OrdinalIgnoreCase);

        return new CriticalSetChange
        {
            Added = after.Except(before, StringComparer.OrdinalIgnoreCase).ToList(),
            Removed = before.Except(after, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}

/// <summary>Resultado de fusionar un barrido con el inventario.</summary>
public sealed record DeviceMerge
{
    public IReadOnlyList<DiscoveredDevice> Devices { get; init; } = Array.Empty<DiscoveredDevice>();

    /// <summary>Los que no se habian visto nunca. Son los que van al Timeline.</summary>
    public IReadOnlyList<DiscoveredDevice> Added { get; init; } = Array.Empty<DiscoveredDevice>();
}

/// <summary>Altas y bajas del conjunto de equipos vigilados.</summary>
public sealed record CriticalSetChange
{
    public IReadOnlyList<string> Added { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Removed { get; init; } = Array.Empty<string>();

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}

/// <summary>
/// Ordena direcciones IP por su valor y no por su texto.
///
/// Lo que no parsea se va al final en vez de reventar: el inventario tiene que poder
/// pintarse aunque una fila traiga basura.
/// </summary>
public sealed class IpAddressOrder : IComparer<string>
{
    public static readonly IpAddressOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        bool left = IPAddress.TryParse(x, out IPAddress? a);
        bool right = IPAddress.TryParse(y, out IPAddress? b);

        if (!left || !right)
        {
            return left == right
                ? StringComparer.OrdinalIgnoreCase.Compare(x, y)
                : left ? -1 : 1;
        }

        byte[] first = a!.GetAddressBytes();
        byte[] second = b!.GetAddressBytes();

        // IPv4 antes que IPv6: en un rig lo que miras son direcciones de cuatro octetos.
        if (first.Length != second.Length)
        {
            return first.Length.CompareTo(second.Length);
        }

        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
            {
                return first[i].CompareTo(second[i]);
            }
        }

        return 0;
    }
}
