using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

public class AudioDiagnosticsTests
{
    private static AudioDevice Device(
        string name,
        int mix,
        int? declared = null,
        bool isDefault = false,
        AudioDeviceState state = AudioDeviceState.Active) => new()
    {
        Id = name,
        FriendlyName = name,
        DataFlow = AudioDataFlow.Render,
        State = state,
        IsDefault = isDefault,
        MixSampleRate = mix,
        DefaultSampleRate = declared,
    };

    [Fact]
    public void Sin_dispositivo_por_defecto_no_sale_audio_de_ningun_sitio()
    {
        AudioDevice[] devices = { Device("Dante Virtual Soundcard", 48000, 48000) };

        Assert.Equal(HealthState.Critical, AudioDiagnostics.Evaluate(devices));
    }

    [Fact]
    public void Un_mismatch_de_sample_rate_es_ambar_no_rojo()
    {
        // La máquina suena; va a crujir. Eso se revisa, no se declara roto.
        AudioDevice[] devices =
        {
            Device("DVS", 48000, 44100, isDefault: true),
        };

        Assert.Equal(HealthState.Warning, AudioDiagnostics.Evaluate(devices));
        Assert.Single(AudioDiagnostics.FindSampleRateMismatches(devices));
    }

    [Fact]
    public void Sin_mismatch_y_con_defecto_esta_todo_bien()
    {
        AudioDevice[] devices =
        {
            Device("DVS", 48000, 48000, isDefault: true),
            Device("HDMI", 48000, 48000),
        };

        Assert.Equal(HealthState.Ok, AudioDiagnostics.Evaluate(devices));
        Assert.Empty(AudioDiagnostics.FindSampleRateMismatches(devices));
    }

    [Fact]
    public void Un_dispositivo_desconectado_no_cuenta_como_mismatch()
    {
        AudioDevice[] devices =
        {
            Device("DVS", 48000, 48000, isDefault: true),
            Device("Interfaz USB", 44100, 48000, state: AudioDeviceState.Unplugged),
        };

        Assert.Equal(HealthState.Ok, AudioDiagnostics.Evaluate(devices));
    }

    [Fact]
    public void Detecta_sample_rates_conviviendo()
    {
        AudioDevice[] devices =
        {
            Device("DVS", 48000, 48000, isDefault: true),
            Device("Interfaz USB", 44100, 44100),
        };

        Assert.Equal(new[] { 44100, 48000 }, AudioDiagnostics.DistinctActiveSampleRates(devices));
    }
}
