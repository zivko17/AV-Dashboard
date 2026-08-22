namespace AvDashboard.Core.Models;

/// <summary>Como se ha encontrado un dispositivo. Se acumulan: un equipo puede aparecer por varias vias.</summary>
[Flags]
public enum DiscoveryMethod
{
    None = 0,
    Arp = 1,
    Icmp = 2,
    Mdns = 4,
    Ssdp = 8,
    NetBios = 16,
    TcpProbe = 32,
    Manual = 64,
}

/// <summary>Clasificacion util del dispositivo, editable a mano y persistente.</summary>
public enum DeviceKind
{
    Unknown = 0,
    Computer,
    AudioDevice,
    VideoDevice,
    LedProcessor,
    NetworkSwitch,
    Projector,
    Camera,
    LightingNode,
    Ups,
    Printer,
    Other,
}

/// <summary>Dispositivo visto en el segmento (M5). El alias es lo que lo hace util.</summary>
public sealed record DiscoveredDevice
{
    public required string Id { get; init; }
    public required string IpAddress { get; init; }
    public string? MacAddress { get; init; }

    /// <summary>Fabricante resuelto por OUI, sin internet.</summary>
    public string? Vendor { get; init; }

    public string? Hostname { get; init; }

    /// <summary>Nombre que le has puesto tu. Es lo que quieres leer a las 19:40, no una IP.</summary>
    public string? Alias { get; init; }

    public DeviceKind Kind { get; init; } = DeviceKind.Unknown;
    public DiscoveryMethod SeenBy { get; init; }

    /// <summary>Servicios mDNS anunciados: _netaudio-arc._udp, _ndi._tcp...</summary>
    public IReadOnlyList<string> MdnsServices { get; init; } = Array.Empty<string>();

    public IReadOnlyList<int> OpenPorts { get; init; } = Array.Empty<int>();

    /// <summary>Puerto de interfaz web detectado, para el boton "abrir interfaz".</summary>
    public int? WebPort { get; init; }

    /// <summary>Si esta marcado como critico entra en el ping continuo y en la checklist pre-show.</summary>
    public bool IsCritical { get; init; }

    public string? Notes { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }

    public string DisplayName => Alias ?? Hostname ?? IpAddress;
}
