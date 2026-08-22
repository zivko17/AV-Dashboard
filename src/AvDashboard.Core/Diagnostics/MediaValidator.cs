using AvDashboard.Core.Models;

namespace AvDashboard.Core.Diagnostics;

/// <summary>
/// Contrasta lo que ffprobe ha leido de un fichero contra los parametros del show (M12).
/// La deteccion y la sugerencia de conversion van juntas: detectar VFR sin ofrecer el
/// boton que lo arregla deja el trabajo a medias.
/// </summary>
public static class MediaValidator
{
    public static MediaAsset Validate(MediaAsset asset, ShowMediaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(spec);

        List<MediaFinding> findings = new();

        if (asset.Video is MediaVideoStream video)
        {
            if (video.IsVariableFrameRate)
            {
                findings.Add(new MediaFinding
                {
                    Code = "media.vfr",
                    Severity = Severity.Error,
                    Message = "Frame rate variable. Va a desincronizar en Resolume y en vMix.",
                    SuggestedPreset = $"cfr-{FormatRate(spec.TargetFrameRate)}",
                });
            }

            if (video.AverageFrameRate is double fps && !RatesMatch(fps, spec.TargetFrameRate))
            {
                findings.Add(new MediaFinding
                {
                    Code = "media.framerate-mismatch",
                    Severity = Severity.Warning,
                    Message = $"Contenido a {FormatRate(fps)} fps en un show a {FormatRate(spec.TargetFrameRate)} fps.",
                    SuggestedPreset = $"cfr-{FormatRate(spec.TargetFrameRate)}",
                });
            }

            if (video.Codec is string codec &&
                spec.DiscouragedCodecs.Contains(codec, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new MediaFinding
                {
                    Code = "media.codec-discouraged",
                    Severity = Severity.Warning,
                    Message = $"Códec «{codec}» poco fiable para reproducción en directo.",
                    SuggestedPreset = "hap-q",
                });
            }

            if (spec.CanvasWidth is int cw && spec.CanvasHeight is int ch &&
                video.Width > 0 && video.Height > 0 &&
                (video.Width != cw || video.Height != ch))
            {
                findings.Add(new MediaFinding
                {
                    Code = "media.resolution-mismatch",
                    Severity = Severity.Warning,
                    Message = $"{video.Width}×{video.Height} no encaja en el canvas {cw}×{ch}.",
                });
            }
        }
        else
        {
            findings.Add(new MediaFinding
            {
                Code = "media.no-video",
                Severity = Severity.Info,
                Message = "El fichero no tiene pista de vídeo.",
            });
        }

        if (asset.Audio is MediaAudioStream audio && audio.SampleRate > 0 &&
            audio.SampleRate != spec.TargetAudioSampleRate)
        {
            findings.Add(new MediaFinding
            {
                Code = "media.audio-samplerate",
                Severity = Severity.Warning,
                Message = $"Audio a {audio.SampleRate} Hz con el sistema a {spec.TargetAudioSampleRate} Hz.",
                SuggestedPreset = $"resample-{spec.TargetAudioSampleRate}",
            });
        }

        return asset with { Findings = findings };
    }

    /// <summary>
    /// Compara framerates con la tolerancia justa para que 29.97 y 30000/1001 sean lo
    /// mismo, pero 25 y 29.97 no.
    /// </summary>
    private static bool RatesMatch(double a, double b) => Math.Abs(a - b) < 0.01;

    private static string FormatRate(double rate) =>
        Math.Abs(rate - Math.Round(rate)) < 0.001
            ? Math.Round(rate).ToString("0")
            : rate.ToString("0.###");
}
