using AvDashboard.Agent.Storage;
using AvDashboard.Agent.Video;
using AvDashboard.Core.Contracts;
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
    private readonly EdidDriftDetector _drift;
    private readonly ILogger<InventoryCollector> _logger;

    private int _lastActiveDisplays = -1;

    /// <summary>Conectores por los que ya se ha avisado, para no repetir cada diez segundos.</summary>
    private readonly HashSet<string> _reportedDrift = new(StringComparer.OrdinalIgnoreCase);

    public InventoryCollector(
        NodeState state,
        EventRepository events,
        EdidDriftDetector drift,
        ILogger<InventoryCollector> logger)
        : base(TimeSpan.FromSeconds(10), logger)
    {
        _state = state;
        _events = events;
        _drift = drift;
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

        await CheckEdidDriftAsync(topology, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Aviso cuando el EDID de una salida deja de coincidir con la referencia guardada.
    ///
    /// Es el sintoma de que alguien ha cambiado un splitter o un procesador: la imagen se
    /// sigue viendo, el modo negociado es otro, y sin esto no te enteras hasta que algo no
    /// encaja tres horas despues.
    /// </summary>
    private async Task CheckEdidDriftAsync(DisplayTopology topology, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdidDrift> drift;

        try
        {
            drift = await _drift.DetectAsync(topology, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            // La base esta ocupada. Se reintenta en el siguiente ciclo; no vale la pena
            // tumbar el colector de inventario por esto.
            _logger.LogDebug(ex, "No se ha podido comprobar la deriva de EDID en este ciclo.");
            return;
        }

        HashSet<string> current = new(drift.Select(d => d.ConnectorKey), StringComparer.OrdinalIgnoreCase);

        foreach (EdidDrift changed in drift)
        {
            if (!_reportedDrift.Add(changed.ConnectorKey))
            {
                continue;
            }

            string message =
                $"El EDID de «{changed.ReferenceName}» ha cambiado: se esperaba {changed.ExpectedFingerprint} " +
                $"y se lee {changed.ActualFingerprint}.";

            _logger.LogWarning("Deriva de EDID en {Connector}: {Message}", changed.ConnectorKey, message);

            await _events.AppendAsync("M18", Sector.Video, Severity.Warning, "edid.changed",
                message, cancellationToken: cancellationToken).ConfigureAwait(false);

            await _events.RaiseAlertAsync("M18", Sector.Video, Severity.Warning, "edid.changed",
                $"EDID distinto del guardado en «{changed.ReferenceName}»", cancellationToken)
                .ConfigureAwait(false);
        }

        // Si vuelve a coincidir —han devuelto el splitter a su sitio— la alerta se cierra.
        if (_reportedDrift.Count > 0 && current.Count == 0)
        {
            _reportedDrift.Clear();
            await _events.ClearAlertAsync("M18", "edid.changed", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _reportedDrift.IntersectWith(current);
        }
    }
}
