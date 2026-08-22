using AvDashboard.Agent.Hubs;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Native.System;
using Microsoft.AspNetCore.SignalR;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Telemetria de sistema a 1 s y ajustes de M1 cada 30 s.
///
/// Los ajustes se leen mucho mas despacio a proposito: nadie cambia el plan de energia
/// entre dos segundos, y cada lectura toca el registro y WMI.
/// </summary>
public sealed class SystemCollector : PeriodicCollector
{
    private readonly SystemTelemetryProvider _telemetry = new();
    private readonly NodeState _state;
    private readonly NodeIdentity _identity;
    private readonly IHubContext<LiveTelemetryHub> _hub;

    private DateTimeOffset _lastSettingsRead = DateTimeOffset.MinValue;
    private static readonly TimeSpan SettingsInterval = TimeSpan.FromSeconds(30);

    public SystemCollector(
        NodeState state,
        NodeIdentity identity,
        AgentOptions options,
        IHubContext<LiveTelemetryHub> hub,
        ILogger<SystemCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.SystemIntervalMs), logger)
    {
        _state = state;
        _identity = identity;
        _hub = hub;
    }

    protected override string Name => "M1";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        SystemStatus status = _telemetry.Read();
        _state.System = status;

        if (DateTimeOffset.UtcNow - _lastSettingsRead > SettingsInterval)
        {
            _state.SystemSettings = RegistrySettingsReader.Read();
            _lastSettingsRead = DateTimeOffset.UtcNow;
        }

        if (LiveTelemetryHub.HasSubscribers(TelemetryChannels.SystemStatus))
        {
            await _hub.Clients
                .Group(TelemetryChannels.SystemStatus)
                .SendAsync(TelemetryChannels.SystemStatus,
                    new SystemTelemetryFrame { NodeId = _identity.Id, Status = status },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
