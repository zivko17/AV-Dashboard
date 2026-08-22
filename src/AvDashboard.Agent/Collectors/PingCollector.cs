using AvDashboard.Agent.Hubs;
using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
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

    private readonly Dictionary<string, PingProbe> _probes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<PingSample>> _windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HealthState> _lastHealth = new(StringComparer.Ordinal);
    private readonly List<(string DeviceId, PingSample Sample)> _pending = new();

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
        ILogger<PingCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.PingIntervalMs), logger)
    {
        _state = state;
        _identity = identity;
        _options = options;
        _hub = hub;
        _repository = repository;
        _events = events;
    }

    protected override string Name => "M5-ping";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        if (_options.CriticalDevices.Count == 0)
        {
            return;
        }

        foreach (CriticalDeviceOptions device in _options.CriticalDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_probes.TryGetValue(device.Address, out PingProbe? probe))
            {
                probe = new PingProbe();
                _probes[device.Address] = probe;
            }

            PingSample sample = await probe
                .SendAsync(device.Address, _options.Telemetry.PingTimeoutMs, cancellationToken)
                .ConfigureAwait(false);

            Record(device.Address, sample);

            PingStatistics stats = ComputeStatistics(device.Alias ?? device.Address, device.Address);
            _state.UpdatePing(device.Address, stats, SnapshotWindow(device.Address));

            await ReportHealthChangeAsync(device, stats, cancellationToken).ConfigureAwait(false);

            if (LiveTelemetryHub.HasSubscribers(TelemetryChannels.PingSamples))
            {
                await _hub.Clients
                    .Group(TelemetryChannels.PingSamples)
                    .SendAsync(TelemetryChannels.PingSamples, new PingFrame
                    {
                        NodeId = _identity.Id,
                        DeviceId = device.Address,
                        Target = device.Alias ?? device.Address,
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
        CriticalDeviceOptions device,
        PingStatistics stats,
        CancellationToken cancellationToken)
    {
        // Con menos de diez muestras cualquier veredicto es ruido.
        if (stats.SampleCount < 10)
        {
            return;
        }

        HealthState health = PingStatisticsCalculator.Evaluate(stats);
        string label = device.Alias ?? device.Address;

        if (_lastHealth.TryGetValue(device.Address, out HealthState previous) && previous == health)
        {
            return;
        }

        _lastHealth[device.Address] = health;

        switch (health)
        {
            case HealthState.Critical:
                await _events.AppendAsync("M5", Sector.Network, Severity.Error, "ping.critical",
                    $"«{label}» no responde o responde muy mal: pérdida {stats.LossPercent:0.#} %.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _events.RaiseAlertAsync("M5", Sector.Network, Severity.Error,
                    $"ping.critical:{device.Address}", $"Sin respuesta fiable de {label}", cancellationToken)
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

                await _events.ClearAlertAsync("M5", $"ping.critical:{device.Address}", cancellationToken)
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
