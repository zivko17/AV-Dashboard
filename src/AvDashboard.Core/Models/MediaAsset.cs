namespace AvDashboard.Core.Models;

/// <summary>Resultado de analizar un fichero de contenido con ffprobe (M12).</summary>
public sealed record MediaAsset
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? ContainerFormat { get; init; }

    public MediaVideoStream? Video { get; init; }
    public MediaAudioStream? Audio { get; init; }

    public IReadOnlyList<MediaFinding> Findings { get; init; } = Array.Empty<MediaFinding>();

    public HealthState Verdict => Findings.Count == 0
        ? HealthState.Ok
        : Findings.Select(f => f.Severity switch
        {
            Severity.Critical or Severity.Error => HealthState.Critical,
            Severity.Warning => HealthState.Warning,
            _ => HealthState.Ok,
        }).Worst();
}

public sealed record MediaVideoStream
{
    public string? Codec { get; init; }
    public string? Profile { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Framerate promedio real (avg_frame_rate).</summary>
    public double? AverageFrameRate { get; init; }

    /// <summary>Framerate nominal declarado (r_frame_rate).</summary>
    public double? NominalFrameRate { get; init; }

    /// <summary>
    /// Frame rate variable. La causa silenciosa de desincronia en Resolume y vMix,
    /// y no se ve en las propiedades del fichero en Windows.
    /// </summary>
    public bool IsVariableFrameRate { get; init; }

    public long? BitRate { get; init; }
    public string? PixelFormat { get; init; }
    public string? ColorSpace { get; init; }
    public string? ColorTransfer { get; init; }
}

public sealed record MediaAudioStream
{
    public string? Codec { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public long? BitRate { get; init; }

    /// <summary>Loudness integrada en LUFS si se ha medido. -23 es el objetivo EBU R128.</summary>
    public double? IntegratedLufs { get; init; }
}

/// <summary>Un problema detectado en el contenido, con la conversion que lo arregla.</summary>
public sealed record MediaFinding
{
    public required string Code { get; init; }
    public required Severity Severity { get; init; }
    public required string Message { get; init; }

    /// <summary>Preset de conversion sugerido, si lo hay. Es el paso que hoy haces a mano.</summary>
    public string? SuggestedPreset { get; init; }
}

/// <summary>Parametros del show contra los que se valida el contenido.</summary>
public sealed record ShowMediaSpec
{
    public double TargetFrameRate { get; init; } = 25;
    public int TargetAudioSampleRate { get; init; } = 48000;

    /// <summary>Resolucion del canvas LED. Un contenido que no encaja se marca.</summary>
    public int? CanvasWidth { get; init; }
    public int? CanvasHeight { get; init; }

    public IReadOnlyList<string> DiscouragedCodecs { get; init; } =
        new[] { "h264_mp4toannexb", "wmv3", "vc1", "mpeg4", "msmpeg4v3" };
}
