using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

public class MediaValidatorTests
{
    private static MediaAsset Asset(MediaVideoStream? video, MediaAudioStream? audio = null) => new()
    {
        Path = @"D:\contenido\clip.mp4",
        FileName = "clip.mp4",
        Video = video,
        Audio = audio,
    };

    private static readonly ShowMediaSpec Spec = new()
    {
        TargetFrameRate = 25,
        TargetAudioSampleRate = 48000,
        CanvasWidth = 1920,
        CanvasHeight = 1080,
    };

    [Fact]
    public void Un_clip_correcto_no_genera_avisos()
    {
        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream { Codec = "hap", Width = 1920, Height = 1080, AverageFrameRate = 25 },
                  new MediaAudioStream { Codec = "pcm_s16le", SampleRate = 48000 }),
            Spec);

        Assert.Empty(result.Findings);
        Assert.Equal(HealthState.Ok, result.Verdict);
    }

    [Fact]
    public void El_frame_rate_variable_es_error_y_propone_conversion()
    {
        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream
            {
                Codec = "h264", Width = 1920, Height = 1080,
                AverageFrameRate = 25, IsVariableFrameRate = true,
            }),
            Spec);

        MediaFinding vfr = Assert.Single(result.Findings, f => f.Code == "media.vfr");
        Assert.Equal(Severity.Error, vfr.Severity);
        Assert.Equal("cfr-25", vfr.SuggestedPreset);
        Assert.Equal(HealthState.Critical, result.Verdict);
    }

    [Fact]
    public void Un_contenido_a_30_en_un_show_a_25_se_marca()
    {
        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream { Codec = "prores", Width = 1920, Height = 1080, AverageFrameRate = 30 }),
            Spec);

        Assert.Contains(result.Findings, f => f.Code == "media.framerate-mismatch");
    }

    [Fact]
    public void Las_resoluciones_que_no_encajan_en_el_canvas_se_marcan()
    {
        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream { Codec = "hap", Width = 3840, Height = 2160, AverageFrameRate = 25 }),
            Spec);

        Assert.Contains(result.Findings, f => f.Code == "media.resolution-mismatch");
    }

    [Fact]
    public void Un_audio_a_44_1_con_el_sistema_a_48_se_marca_con_su_preset()
    {
        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream { Codec = "hap", Width = 1920, Height = 1080, AverageFrameRate = 25 },
                  new MediaAudioStream { Codec = "aac", SampleRate = 44100 }),
            Spec);

        MediaFinding finding = Assert.Single(result.Findings, f => f.Code == "media.audio-samplerate");
        Assert.Equal("resample-48000", finding.SuggestedPreset);
    }

    [Fact]
    public void Los_framerates_ntsc_no_se_marcan_por_redondeo()
    {
        ShowMediaSpec ntsc = Spec with { TargetFrameRate = 30000.0 / 1001 };

        MediaAsset result = MediaValidator.Validate(
            Asset(new MediaVideoStream
            {
                Codec = "hap", Width = 1920, Height = 1080, AverageFrameRate = 29.97,
            }),
            ntsc);

        Assert.DoesNotContain(result.Findings, f => f.Code == "media.framerate-mismatch");
    }
}
