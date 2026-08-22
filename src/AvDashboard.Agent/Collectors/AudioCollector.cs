using AvDashboard.Agent.Hubs;
using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using AvDashboard.Native.Audio;
using Microsoft.AspNetCore.SignalR;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Audio (M3 base): enumeracion cada 3 s, medidores a 20 fps solo si alguien mira.
///
/// La enumeracion completa activa un IAudioClient por dispositivo, asi que no puede ir
/// al ritmo de los medidores. Los medidores reutilizan objetos COM cacheados y son baratos.
/// </summary>
public sealed class AudioCollector : PeriodicCollector
{
    private readonly NodeState _state;
    private readonly NodeIdentity _identity;
    private readonly IHubContext<LiveTelemetryHub> _hub;
    private readonly EventRepository _events;
    private readonly ILogger<AudioCollector> _logger;

    private CoreAudioProvider? _provider;
    private DateTimeOffset _lastEnumeration = DateTimeOffset.MinValue;
    private HashSet<string> _knownActiveIds = new(StringComparer.Ordinal);

    private static readonly TimeSpan EnumerationInterval = TimeSpan.FromSeconds(3);

    public AudioCollector(
        NodeState state,
        NodeIdentity identity,
        AgentOptions options,
        IHubContext<LiveTelemetryHub> hub,
        EventRepository events,
        ILogger<AudioCollector> logger)
        : base(TimeSpan.FromMilliseconds(options.Telemetry.AudioMeterIntervalMs), logger)
    {
        _state = state;
        _identity = identity;
        _hub = hub;
        _events = events;
        _logger = logger;
    }

    protected override string Name => "M3";

    protected override async Task CollectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _provider ??= new CoreAudioProvider();

        if (DateTimeOffset.UtcNow - _lastEnumeration > EnumerationInterval)
        {
            await EnumerateAsync(cancellationToken).ConfigureAwait(false);
            _lastEnumeration = DateTimeOffset.UtcNow;
        }

        if (!LiveTelemetryHub.HasSubscribers(TelemetryChannels.AudioMeters))
        {
            return;
        }

        string[] activeIds = _state.AudioDevices
            .Where(d => d.State == AudioDeviceState.Active)
            .Select(d => d.Id)
            .ToArray();

        if (activeIds.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, float> peaks = _provider.ReadPeaks(activeIds);

        await _hub.Clients
            .Group(TelemetryChannels.AudioMeters)
            .SendAsync(TelemetryChannels.AudioMeters, new AudioMeterFrame
            {
                NodeId = _identity.Id,
                Timestamp = DateTimeOffset.UtcNow,
                Readings = peaks
                    .Select(p => new AudioMeterReading { Id = p.Key, Peak = p.Value })
                    .ToList(),
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnumerateAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AudioDevice> devices = _provider!.GetDevices();
        _state.AudioDevices = devices;

        if (_state.AsioDrivers.Count == 0 && OperatingSystem.IsWindows())
        {
            _state.AsioDrivers = AsioRegistry.GetDrivers();
        }

        HashSet<string> activeIds = devices
            .Where(d => d.State == AudioDeviceState.Active)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Watchdog de dispositivo desaparecido: si un endpoint que estaba activo deja de
        // estarlo, es un evento del Timeline. En un rig con Dante Virtual Soundcard esto
        // es lo primero que pasa cuando la red se cae.
        foreach (string missing in _knownActiveIds.Except(activeIds))
        {
            AudioDevice? device = devices.FirstOrDefault(d => d.Id == missing);
            string label = device?.FriendlyName ?? missing;

            _logger.LogWarning("El dispositivo de audio «{Device}» ha dejado de estar activo.", label);

            await _events.AppendAsync("M3", Sector.Audio, Severity.Error, "audio.device-lost",
                $"El dispositivo «{label}» ha dejado de estar activo.",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _events.RaiseAlertAsync("M3", Sector.Audio, Severity.Error, "audio.device-lost",
                $"Dispositivo de audio desaparecido: {label}", cancellationToken).ConfigureAwait(false);
        }

        if (activeIds.Count > 0 && _knownActiveIds.Count > 0 && activeIds.IsSupersetOf(_knownActiveIds))
        {
            await _events.ClearAlertAsync("M3", "audio.device-lost", cancellationToken).ConfigureAwait(false);
        }

        _knownActiveIds = activeIds;

        // Mismatch de sample rate: causa numero uno de crujidos y no se ve en ninguna
        // pantalla de Windows. Se registra una vez por cambio, no en cada ciclo.
        List<AudioDevice> mismatched = AudioDiagnostics.FindSampleRateMismatches(devices).ToList();
        if (mismatched.Count > 0)
        {
            await _events.RaiseAlertAsync("M3", Sector.Audio, Severity.Warning, "audio.samplerate-mismatch",
                "Mismatch de sample rate en: " + string.Join(", ", mismatched.Select(d => d.FriendlyName)),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _events.ClearAlertAsync("M3", "audio.samplerate-mismatch", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
