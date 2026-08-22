using AvDashboard.Agent.Storage;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native.System;
using AvDashboard.Native.Video;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Lo que cambia despacio: unidades de disco y topologia de displays, cada 10 s.
///
/// La deteccion de display desaparecido esta aqui y no en un modulo de vídeo completo
/// porque es el aviso que hace falta desde el primer dia: en una pared LED significa
/// que has perdido la pared y todavia no lo sabes.
/// </summary>
public sealed class InventoryCollector : PeriodicCollector
{
    private readonly NodeState _state;
    private readonly EventRepository _events;
    private readonly ILogger<InventoryCollector> _logger;

    private int _lastActiveDisplays = -1;

    public InventoryCollector(
        NodeState state,
        EventRepository events,
        ILogger<InventoryCollector> logger)
        : base(TimeSpan.FromSeconds(10), logger)
    {
        _state = state;
        _events = events;
        _logger = logger;
    }

    protected override string Name => "M9/M4";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        _state.Volumes = StorageProvider.GetVolumes();

        DisplayTopology topology = DisplayTopologyProvider.GetTopology();
        _state.Displays = topology;

        if (_lastActiveDisplays >= 0 && topology.ActiveCount < _lastActiveDisplays)
        {
            _logger.LogError("Se ha perdido un display: de {Before} a {After} salidas activas.",
                _lastActiveDisplays, topology.ActiveCount);

            await _events.AppendAsync("M4", Sector.Video, Severity.Critical, "display.lost",
                $"Se ha perdido un display: de {_lastActiveDisplays} a {topology.ActiveCount} salidas activas.",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _events.RaiseAlertAsync("M4", Sector.Video, Severity.Critical, "display.lost",
                $"Display desaparecido ({topology.ActiveCount} activos)", cancellationToken)
                .ConfigureAwait(false);
        }
        else if (_lastActiveDisplays >= 0 && topology.ActiveCount > _lastActiveDisplays)
        {
            await _events.AppendAsync("M4", Sector.Video, Severity.Info, "display.found",
                $"Nueva salida detectada: {topology.ActiveCount} displays activos.",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _events.ClearAlertAsync("M4", "display.lost", cancellationToken).ConfigureAwait(false);
        }

        _lastActiveDisplays = topology.ActiveCount;
    }
}
