using AvDashboard.Agent.Hubs;
using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native.Network;
using Microsoft.AspNetCore.SignalR;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Ping continuo a los dispositivos criticos (M5).
///
/// El modulo que mas veces te va a salvar: la subida de latencia a un procesador LED
/// aparece aqui antes de que se vea nada en la pared, y queda en el Timeline con su
/// timestamp para correlarla despues con lo que pasó.
/// </summary>
public sealed class PingCollector : PeriodicCollector
{
    private readonly NodeState _state;
    private readonly NodeIdentity _identity;
    private readonly AgentOptions _options;
    private readonly IHubContext<LiveTelemetryHub> _hub;
    private readonly PingRepository _repository;
    private readonly EventRepository _events;

    private readonly DeviceRepository _devices;

    private readonly Dictionary<string, PingProbe> _probes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<PingSample>> _windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HealthState> _lastHealth = new(StringComparer.Ordinal);
    private readonly List<(string DeviceId, PingSample Sample)> _pending = new();

    /// <summary>
    /// Direcciones que han contestado desde el ultimo vuelco a disco.
    ///
    /// Su `last_seen` se actualiza cuando se vuelca el lote, no en cada respuesta: una
    /// escritura por segundo y por equipo a SQLite para mover una fecha seria pagar disco a
    /// cambio de una precision que nadie mira.
    /// </summary>
    private readonly HashSet<string> _answered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ventana en vivo para el grafico del dashboard: 15 minutos a 1 s.</summary>
    private const int WindowSize = 900;

    /// <summary>Muestras que se acumulan antes de tocar el disco.</summary>
    private const int FlushThreshold = 30;

    public PingCollector(
        NodeState state,
        NodeIdentity identity,
        AgentOptions options,
        IHubContext<LiveTelemetryHub> hub,
        PingRepository repository,
        EventRepository events,
        DeviceRepository devices,
        ILogger<PingCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.PingIntervalMs), logger)
    {
        _state = state;
        _identity = identity;
        _options = options;
        _hub = hub;
        _repository = repository;
        _events = events;
        _devices = devices;
    }

    protected override string Name => "M5-ping";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        // La lista se relee en cada vuelta en lugar de cachearse. Es un SELECT sobre un
        // indice parcial con decenas de filas, al lado de un ICMP con timeout de 800 ms:
        // ruido de fondo. Cachearla ahorraria esa consulta a cambio de dos fuentes de
        // verdad que se desincronizan, y con ellas toda la familia de «lo he anadido y no
        // aparece».
        IReadOnlyDictionary<string, string?> watched =
            await _devices.ListCriticalAliasesAsync(cancellationToken).ConfigureAwait(false);

        await RetireAsync(
            DeviceInventory.Diff(_probes.Keys.ToList(), watched.Keys.ToList()),
            cancellationToken).ConfigureAwait(false);

        if (watched.Count == 0)
        {
            return;
        }

        foreach ((string address, string? alias) in watched)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_probes.TryGetValue(address, out PingProbe? probe))
            {
                probe = new PingProbe();
                _probes[address] = probe;
            }

            PingSample sample = await probe
                .SendAsync(address, _options.Telemetry.PingTimeoutMs, cancellationToken)
                .ConfigureAwait(false);

            Record(address, sample);

            if (sample.RttMs is not null)
            {
                _answered.Add(address);
            }

            PingStatistics stats = ComputeStatistics(alias ?? address, address);
            _state.UpdatePing(address, stats, SnapshotWindow(address));

            await ReportHealthChangeAsync(address, alias, stats, cancellationToken).ConfigureAwait(false);

            if (LiveTelemetryHub.HasSubscribers(TelemetryChannels.PingSamples))
            {
                await _hub.Clients
                    .Group(TelemetryChannels.PingSamples)
                    .SendAsync(TelemetryChannels.PingSamples, new PingFrame
                    {
                        NodeId = _identity.Id,
                        DeviceId = address,
                        Target = alias ?? address,
                        Sample = sample,
                    }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (_pending.Count >= FlushThreshold)
        {
            List<(string, PingSample)> batch = new(_pending);
            _pending.Clear();
            await _repository.AppendBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            if (_answered.Count > 0)
            {
                List<string> seen = _answered.ToList();
                _answered.Clear();
                await _devices.TouchAsync(seen, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Suelta lo que dejo un equipo al que ya no se vigila.
    ///
    /// Son cuatro cosas y la ultima es la que se olvida: la alerta activa. El colector solo
    /// baja las alertas de los equipos que sigue recorriendo, asi que uno borrado estando en
    /// rojo dejaria la suya encendida para siempre, sin nadie que pudiera bajarla y sin
    /// forma de saber de que era.
    /// </summary>
    private async Task RetireAsync(CriticalSetChange change, CancellationToken cancellationToken)
    {
        foreach (string address in change.Removed)
        {
            if (_probes.Remove(address, out PingProbe? probe))
            {
                probe.Dispose();
            }

            _windows.Remove(address);
            _lastHealth.Remove(address);
            _answered.Remove(address);
            _pending.RemoveAll(entry => string.Equals(
                entry.DeviceId, address, StringComparison.OrdinalIgnoreCase));

            // Si no, el equipo sigue en la tabla de latencia con sus ultimos datos
            // congelados, que es peor que no salir: parece que responde.
            _state.RemovePing(address);

            await _events.ClearAlertAsync("M5", $"ping.critical:{address}", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void Record(string address, PingSample sample)
    {
        if (!_windows.TryGetValue(address, out Queue<PingSample>? window))
        {
            window = new Queue<PingSample>(WindowSize);
            _windows[address] = window;
        }

        window.Enqueue(sample);
        while (window.Count > WindowSize)
        {
            window.Dequeue();
        }

        _pending.Add((address, sample));
    }

    private IReadOnlyList<PingSample> SnapshotWindow(string address) =>
        _windows.TryGetValue(address, out Queue<PingSample>? window)
            ? window.ToArray()
            : Array.Empty<PingSample>();

    private PingStatistics ComputeStatistics(string label, string address) =>
        _windows.TryGetValue(address, out Queue<PingSample>? window)
            ? PingStatisticsCalculator.Compute(label, window.ToArray())
            : PingStatisticsCalculator.Compute(label, Array.Empty<PingSample>());

    /// <summary>
    /// Solo se registra el cambio de estado, no cada muestra. Un dispositivo en rojo
    /// durante diez minutos es un evento, no seiscientos.
    /// </summary>
    private async Task ReportHealthChangeAsync(
        string address,
        string? alias,
        PingStatistics stats,
        CancellationToken cancellationToken)
    {
        // Con menos de diez muestras cualquier veredicto es ruido. El umbral vive en Core
        // porque la columna «visto» del inventario tiene que callarse exactamente cuando se
        // calla esto: cuando estaban en dos sitios, la fila pintaba «no responde» en rojo a
        // un equipo recien anadido que estaba perfectamente.
        if (stats.SampleCount < DeviceSeenStates.MinimumSamples)
        {
            return;
        }

        HealthState health = PingStatisticsCalculator.Evaluate(stats);
        string label = alias ?? address;

        if (_lastHealth.TryGetValue(address, out HealthState previous) && previous == health)
        {
            return;
        }

        _lastHealth[address] = health;

        switch (health)
        {
            case HealthState.Critical:
                await _events.AppendAsync("M5", Sector.Network, Severity.Error, "ping.critical",
                    $"«{label}» no responde o responde muy mal: pérdida {stats.LossPercent:0.#} %.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _events.RaiseAlertAsync("M5", Sector.Network, Severity.Error,
                    $"ping.critical:{address}", $"Sin respuesta fiable de {label}", cancellationToken)
                    .ConfigureAwait(false);
                break;

            case HealthState.Warning:
                await _events.AppendAsync("M5", Sector.Network, Severity.Warning, "ping.degraded",
                    $"«{label}» degradado: {stats.AverageMs:0.#} ms de media, jitter {stats.JitterMs:0.#} ms.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;

            case HealthState.Ok:
                await _events.AppendAsync("M5", Sector.Network, Severity.Info, "ping.recovered",
                    $"«{label}» ha vuelto a la normalidad.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _events.ClearAlertAsync("M5", $"ping.critical:{address}", cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    public override void Dispose()
    {
        foreach (PingProbe probe in _probes.Values)
        {
            probe.Dispose();
        }

        _probes.Clear();
        base.Dispose();
    }
}
