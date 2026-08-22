using System.Collections.ObjectModel;
using AvDashboard.Console.Services;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Audio (M3). Se suscribe a los medidores solo mientras la vista esta abierta, y se
/// da de baja al salir: el agente no debe trabajar para una pantalla que nadie mira.
/// </summary>
public sealed partial class AudioViewModel : PageViewModel
{
    private IDisposable? _meterSubscription;

    public AudioViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Audio";
    public override string Glyph => "\uE767";

    /// <summary>
    /// Los endpoints en uso. Van en su propia tarjeta y arriba porque son los únicos que
    /// pueden estar mal: un dispositivo que Windows recuerda pero que no está enchufado no
    /// tiene formato en uso ni nivel, y mezclarlo con los activos convierte una tabla de
    /// cinco filas útiles en una de veinticinco donde no se distingue nada.
    /// </summary>
    public ObservableCollection<AudioDeviceRow> ActiveDevices { get; } = new();

    /// <summary>
    /// Los que Windows tiene registrados y ahora mismo no están: desenchufados, apagados o
    /// de una tarjeta que ya no está. Se enseñan porque son la respuesta a «¿dónde está la
    /// Focusrite?», pero plegados y sin ocupar la pantalla.
    /// </summary>
    public ObservableCollection<AudioDeviceRow> InactiveDevices { get; } = new();

    public ObservableCollection<AsioDriver> AsioDrivers { get; } = new();

    [ObservableProperty]
    private string _mismatchSummary = string.Empty;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _inactiveCount;

    public override async Task ActivateAsync()
    {
        await base.ActivateAsync().ConfigureAwait(true);

        TelemetryClient telemetry = Nodes.Telemetry();
        await telemetry.StartAsync().ConfigureAwait(true);

        _meterSubscription = telemetry.On<AudioMeterFrame>(TelemetryChannels.AudioMeters, OnMeters);
        await telemetry.SubscribeAsync(TelemetryChannels.AudioMeters).ConfigureAwait(true);
    }

    public override async Task DeactivateAsync()
    {
        _meterSubscription?.Dispose();
        _meterSubscription = null;

        await Nodes.Telemetry().UnsubscribeAsync(TelemetryChannels.AudioMeters).ConfigureAwait(true);
        await base.DeactivateAsync().ConfigureAwait(true);
    }

    protected override async Task LoadAsync()
    {
        ApiResult<AudioResponse> result = await Nodes.Api().GetAudioAsync().ConfigureAwait(true);
        if (!Apply(result, out AudioResponse audio))
        {
            return;
        }

        Health = audio.Health;

        ActiveDevices.Clear();
        InactiveDevices.Clear();

        foreach (AudioDevice device in audio.Devices.OrderByDescending(d => d.IsDefault)
                     .ThenBy(d => d.DataFlow)
                     .ThenBy(d => d.FriendlyName, StringComparer.CurrentCulture))
        {
            AudioDeviceRow row = new(device);

            if (device.State == AudioDeviceState.Active)
            {
                ActiveDevices.Add(row);
            }
            else
            {
                InactiveDevices.Add(row);
            }
        }

        ActiveCount = ActiveDevices.Count;
        InactiveCount = InactiveDevices.Count;

        AsioDrivers.Clear();
        foreach (AsioDriver driver in audio.AsioDrivers)
        {
            AsioDrivers.Add(driver);
        }

        MismatchSummary = audio.SampleRateMismatches.Count == 0
            ? "Todos los endpoints activos coinciden con su formato declarado."
            : "Mismatch de sample rate en: " + string.Join(", ", audio.SampleRateMismatches);
    }

    private void OnMeters(AudioMeterFrame frame)
    {
        // El hub entrega en un hilo del pool; la coleccion esta enlazada a la UI.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Solo se buscan en los activos: un endpoint desenchufado no entrega nivel, y
            // recorrer la lista larga por cada trama de medidores es trabajo tirado.
            foreach (AudioMeterReading reading in frame.Readings)
            {
                AudioDeviceRow? row = ActiveDevices.FirstOrDefault(d => d.Id == reading.Id);
                row?.UpdatePeak(reading.Peak);
            }
        });
    }
}

/// <summary>Fila de la tabla de dispositivos, con el medidor en vivo encima.</summary>
public sealed partial class AudioDeviceRow : ObservableObject
{
    public AudioDeviceRow(AudioDevice device)
    {
        Device = device;
    }

    public AudioDevice Device { get; }

    public string Id => Device.Id;
    public string Name => Device.FriendlyName;
    public string Direction => Device.DataFlow == AudioDataFlow.Render ? "Salida" : "Entrada";
    public bool IsDefault => Device.IsDefault;

    public string Format => Device.MixSampleRate > 0
        ? $"{Device.MixSampleRate / 1000.0:0.#} kHz · {Device.MixBitDepth} bit · {Device.MixChannels} ch"
        : "—";

    public string Declared => Device.DefaultSampleRate is int rate
        ? $"{rate / 1000.0:0.#} kHz"
        : "—";

    public bool HasMismatch => Device.HasSampleRateMismatch;

    /// <summary>Por qué este dispositivo no está en uso. Solo se pinta en la tabla de abajo.</summary>
    public string StateText => Device.State switch
    {
        AudioDeviceState.Unplugged => "desenchufado",
        AudioDeviceState.Disabled => "deshabilitado",
        AudioDeviceState.NotPresent => "no presente",
        AudioDeviceState.Active => "activo",
        _ => "desconocido",
    };

    public HealthState Health => Device.State switch
    {
        AudioDeviceState.Active when Device.HasSampleRateMismatch => HealthState.Warning,
        AudioDeviceState.Active => HealthState.Ok,
        AudioDeviceState.Unplugged or AudioDeviceState.NotPresent => HealthState.Unknown,
        _ => HealthState.Unknown,
    };

    [ObservableProperty]
    private float _peak;

    public void UpdatePeak(float peak) => Peak = peak;
}
