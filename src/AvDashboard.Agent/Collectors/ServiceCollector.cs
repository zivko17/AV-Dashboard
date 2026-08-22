using AvDashboard.Agent.Storage;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native.System;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Inventario de servicios cada 15 s (M6).
///
/// Un servicio AV esperado que se para genera alerta; uno parado que debe estarlo, no.
/// Esa distincion la define el perfil activo y es toda la gracia del contador por sector.
/// </summary>
public sealed class ServiceCollector : PeriodicCollector
{
    private readonly NodeState _state;
    private readonly EventRepository _events;
    private readonly WindowsServiceReader _reader;

    private HashSet<string> _reportedStopped = new(StringComparer.OrdinalIgnoreCase);

    public ServiceCollector(
        NodeState state,
        AgentOptions options,
        EventRepository events,
        ServiceTaxonomy taxonomy,
        ILogger<ServiceCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.ServiceIntervalMs), logger)
    {
        _state = state;
        _events = events;
        _reader = new WindowsServiceReader(taxonomy);
    }

    protected override string Name => "M6";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceInfo> services = _reader.GetServices();
        _state.Services = services;

        HashSet<string> stopped = services
            .Where(s => s.IsUnexpectedlyStopped)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string name in stopped.Except(_reportedStopped))
        {
            ServiceInfo service = services.First(s => s.Name == name);

            await _events.AppendAsync("M6", service.Sector, Severity.Error, "service.stopped",
                $"El servicio «{service.DisplayName}» se esperaba en marcha y está {Describe(service.State)}.",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _events.RaiseAlertAsync("M6", service.Sector, Severity.Error, $"service.stopped:{name}",
                $"Servicio detenido: {service.DisplayName}", cancellationToken).ConfigureAwait(false);
        }

        foreach (string name in _reportedStopped.Except(stopped))
        {
            await _events.ClearAlertAsync("M6", $"service.stopped:{name}", cancellationToken)
                .ConfigureAwait(false);
        }

        _reportedStopped = stopped;
    }

    private static string Describe(ServiceState state) => state switch
    {
        ServiceState.Stopped => "detenido",
        ServiceState.Paused => "en pausa",
        ServiceState.StartPending => "arrancando",
        ServiceState.StopPending => "parándose",
        _ => "en estado desconocido",
    };
}
