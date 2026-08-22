namespace AvDashboard.Core.Models;

/// <summary>Una interfaz de red con lo que hace falta antes de montar Dante o NDI (M5).</summary>
public sealed record NetworkInterfaceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? MacAddress { get; init; }

    public bool IsUp { get; init; }

    /// <summary>Velocidad de enlace en bits por segundo. Un gigabit que negocia a 100 Mb se ve aqui.</summary>
    public long LinkSpeedBps { get; init; }

    public int Mtu { get; init; }
    public bool IsDhcp { get; init; }

    public IReadOnlyList<string> Ipv4Addresses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Ipv6Addresses { get; init; } = Array.Empty<string>();
    public string? SubnetMask { get; init; }
    public IReadOnlyList<string> Gateways { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Power management del adaptador (PnPCapabilities). Cuando esta activo tumba
    /// Dante y NDI en silencio y sin dejar rastro en ningun log.
    /// </summary>
    public bool? PowerManagementEnabled { get; init; }

    /// <summary>Nagle desactivado en esta interfaz (TcpAckFrequency y TCPNoDelay).</summary>
    public bool? NagleDisabled { get; init; }

    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }

    /// <summary>Throughput instantaneo calculado entre dos muestras consecutivas.</summary>
    public double SendMbps { get; init; }
    public double ReceiveMbps { get; init; }

    /// <summary>Trafico multicast entrante. El aviso temprano de una tormenta.</summary>
    public double MulticastPps { get; init; }
    public double MulticastMbps { get; init; }
}

/// <summary>Perfil de IP estatica conmutable (M5). Sustituye al `setip` de av-light-tools.</summary>
public sealed record NetworkProfile
{
    public required string Name { get; init; }

    /// <summary>Interfaz a la que aplica. Null = se pregunta al aplicar.</summary>
    public string? InterfaceName { get; init; }

    public bool UseDhcp { get; init; }
    public string? IpAddress { get; init; }
    public string? SubnetMask { get; init; }
    public string? Gateway { get; init; }
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
}

/// <summary>Cambio de estado de enlace. Un SFP inestable o un cable pisado se ven aqui primero.</summary>
public sealed record LinkFlapEvent
{
    public required string InterfaceId { get; init; }
    public required string InterfaceName { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsUp { get; init; }
    public long? LinkSpeedBps { get; init; }
}
