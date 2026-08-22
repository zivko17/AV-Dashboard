using AvDashboard.Agent.Hubs;
using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native.Network;
using Microsoft.AspNetCore.SignalR;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Interfaces y throughput a 1 s (M5 base), mas deteccion de link flap.
///
/// El link flap es lo que delata un SFP inestable o un cable pisado, y se ve aqui antes
/// que en el show. Cada cambio de estado va al Timeline con su timestamp exacto.
/// </summary>
public sealed class NetworkCollector : PeriodicCollector
{
    private readonly NetworkInterfaceProvider _provider = new();
    private readonly NodeState _state;
    private readonly NodeIdentity _identity;
    private readonly IHubContext<LiveTelemetryHub> _hub;
    private readonly EventRepository _events;

    private readonly Dictionary<string, bool> _linkState = new(StringComparer.Ordinal);

    public NetworkCollector(
        NodeState state,
        NodeIdentity identity,
        AgentOptions options,
        IHubContext<LiveTelemetryHub> hub,
        EventRepository events,
        ILogger<NetworkCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.NetworkIntervalMs), logger)
    {
        _state = state;
        _identity = identity;
        _hub = hub;
        _events = events;
    }

    protected override string Name => "M5";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NetworkInterfaceInfo> interfaces = _provider.GetInterfaces();
        _state.Interfaces = interfaces;

        await DetectLinkFlapsAsync(interfaces, cancellationToken).ConfigureAwait(false);

        if (LiveTelemetryHub.HasSubscribers(TelemetryChannels.NetworkThroughput))
        {
            await _hub.Clients
                .Group(TelemetryChannels.NetworkThroughput)
                .SendAsync(TelemetryChannels.NetworkThroughput, new ThroughputFrame
                {
                    NodeId = _identity.Id,
                    Timestamp = DateTimeOffset.UtcNow,
                    Interfaces = interfaces.Select(i => new InterfaceThroughput
                    {
                        InterfaceId = i.Id,
                        SendMbps = i.SendMbps,
                        ReceiveMbps = i.ReceiveMbps,
                        MulticastMbps = i.MulticastMbps,
                        MulticastPps = i.MulticastPps,
                    }).ToList(),
                }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task DetectLinkFlapsAsync(
        IReadOnlyList<NetworkInterfaceInfo> interfaces,
        CancellationToken cancellationToken)
    {
        foreach (NetworkInterfaceInfo nic in interfaces)
        {
            if (!_linkState.TryGetValue(nic.Id, out bool previous))
            {
                _linkState[nic.Id] = nic.IsUp;
                continue;
            }

            if (previous == nic.IsUp)
            {
                continue;
            }

            _linkState[nic.Id] = nic.IsUp;

            string message = nic.IsUp
                ? $"El enlace de «{nic.Name}» ha subido a {FormatSpeed(nic.LinkSpeedBps)}."
                : $"El enlace de «{nic.Name}» ha caído.";

            await _events.AppendAsync("M5", Sector.Network,
                nic.IsUp ? Severity.Warning : Severity.Error,
                nic.IsUp ? "network.link-up" : "network.link-down",
                message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gb/s",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:0} Mb/s",
        > 0 => $"{bitsPerSecond} b/s",
        _ => "velocidad desconocida",
    };
}
