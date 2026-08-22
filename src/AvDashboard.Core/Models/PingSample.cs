namespace AvDashboard.Core.Models;

/// <summary>Una muestra de ping. El grano fino del modulo que mas veces te salva.</summary>
public readonly record struct PingSample
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Round trip en milisegundos. Null cuando la muestra se perdio.</summary>
    public double? RttMs { get; init; }

    public bool Lost => RttMs is null;
}

/// <summary>Estadistica agregada de una ventana de muestras de ping.</summary>
public sealed record PingStatistics
{
    public required string Target { get; init; }
    public int SampleCount { get; init; }
    public int LostCount { get; init; }

    public double LossPercent => SampleCount == 0 ? 0 : LostCount * 100.0 / SampleCount;

    public double? MinMs { get; init; }
    public double? MaxMs { get; init; }
    public double? AverageMs { get; init; }

    /// <summary>
    /// Jitter medio segun RFC 3550: media de la diferencia absoluta entre muestras
    /// consecutivas. Es mas representativo de lo que sufre el audio que la desviacion tipica.
    /// </summary>
    public double? JitterMs { get; init; }

    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
}
