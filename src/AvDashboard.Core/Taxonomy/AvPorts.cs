namespace AvDashboard.Core.Taxonomy;

/// <summary>Un puerto o rango conocido del mundo AV, con el sector al que pertenece.</summary>
public sealed record AvPort
{
    public required string Service { get; init; }
    public required int From { get; init; }
    public int To { get; init; }
    public required PortProtocol Protocol { get; init; }
    public Sector Sector { get; init; } = Sector.Unclassified;
    public string? Notes { get; init; }

    public bool IsRange => To > From;
    public bool Matches(int port, PortProtocol protocol) =>
        (Protocol & protocol) != 0 && port >= From && port <= (IsRange ? To : From);

    public override string ToString() =>
        IsRange ? $"{Service} {From}-{To}/{Protocol}" : $"{Service} {From}/{Protocol}";
}

[Flags]
public enum PortProtocol
{
    None = 0,
    Tcp = 1,
    Udp = 2,
    Both = Tcp | Udp,
}

/// <summary>
/// Preset "AV Ports" del modulo de firewall (M2) y catalogo para la deteccion
/// proactiva de reglas que bloquean puertos AV conocidos.
/// Siempre preferible a desactivar el firewall entero.
/// </summary>
public static class AvPorts
{
    public static IReadOnlyList<AvPort> Catalog { get; } = new AvPort[]
    {
        new() { Service = "vMix API",            From = 8088, Protocol = PortProtocol.Tcp,  Sector = Sector.Production },
        new() { Service = "vMix Web Controller", From = 8088, To = 8089, Protocol = PortProtocol.Tcp, Sector = Sector.Production },
        new() { Service = "OBS WebSocket",       From = 4455, Protocol = PortProtocol.Tcp,  Sector = Sector.Production },
        new() { Service = "Resolume REST",       From = 8080, Protocol = PortProtocol.Tcp,  Sector = Sector.Production },
        new() { Service = "Resolume OSC",        From = 7000, Protocol = PortProtocol.Udp,  Sector = Sector.Production },
        new() { Service = "Companion",           From = 8000, Protocol = PortProtocol.Tcp,  Sector = Sector.Production },
        new() { Service = "Watchout",            From = 3040, Protocol = PortProtocol.Tcp,  Sector = Sector.Production },

        new() { Service = "mDNS",                From = 5353, Protocol = PortProtocol.Udp,  Sector = Sector.Network,
                Notes = "Descubrimiento de NDI, Dante y Brompton" },
        new() { Service = "NDI streams",         From = 5960, To = 5990, Protocol = PortProtocol.Both, Sector = Sector.Video },
        new() { Service = "NDI Discovery Server",From = 5959, Protocol = PortProtocol.Tcp,  Sector = Sector.Video },

        new() { Service = "Dante control",       From = 4440, To = 4455, Protocol = PortProtocol.Udp, Sector = Sector.Audio },
        new() { Service = "Dante audio",         From = 8700, To = 8708, Protocol = PortProtocol.Udp, Sector = Sector.Audio },
        new() { Service = "PTPv1",               From = 319,  To = 320,  Protocol = PortProtocol.Udp, Sector = Sector.Audio,
                Notes = "Sync de Dante nativo y AES67" },

        new() { Service = "Art-Net",             From = 6454, Protocol = PortProtocol.Udp,  Sector = Sector.Video },
        new() { Service = "sACN (E1.31)",        From = 5568, Protocol = PortProtocol.Udp,  Sector = Sector.Video },

        new() { Service = "Tailscale",           From = 41641, Protocol = PortProtocol.Udp, Sector = Sector.Network },
        new() { Service = "AV Dashboard agent",  From = 47821, Protocol = PortProtocol.Tcp, Sector = Sector.Network },
    };

    /// <summary>Puerto por defecto del agente. Bind exclusivo a loopback y a la IP de Tailscale.</summary>
    public const int AgentPort = 47821;

    /// <summary>Servicios AV que declaran usar este puerto. Vacio si no es un puerto conocido.</summary>
    public static IEnumerable<AvPort> Lookup(int port, PortProtocol protocol = PortProtocol.Both) =>
        Catalog.Where(p => p.Matches(port, protocol));

    /// <summary>Puertos que merece la pena sondear al descubrir un dispositivo.</summary>
    public static IReadOnlyList<int> CommonWebPorts { get; } = new[] { 80, 443, 8080, 8000, 8088 };
}
