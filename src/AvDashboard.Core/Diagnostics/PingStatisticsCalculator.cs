using AvDashboard.Core.Models;

namespace AvDashboard.Core.Diagnostics;

/// <summary>
/// Agrega muestras de ping en latencia, jitter y perdida. Logica pura: el ping
/// continuo es el modulo que mas veces salva un evento y conviene que sus numeros
/// esten cubiertos por pruebas y no salgan de una formula improvisada.
/// </summary>
public static class PingStatisticsCalculator
{
    public static PingStatistics Compute(string target, IReadOnlyList<PingSample> samples)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return new PingStatistics
            {
                Target = target,
                SampleCount = 0,
                LostCount = 0,
                WindowStart = default,
                WindowEnd = default,
            };
        }

        int lost = 0;
        double sum = 0;
        int received = 0;
        double min = double.MaxValue;
        double max = double.MinValue;

        // Jitter segun RFC 3550: media de |rtt(n) - rtt(n-1)| entre muestras recibidas
        // consecutivas. Una perdida rompe la cadena en vez de contar como salto enorme,
        // que es lo que haria una desviacion tipica ingenua.
        double jitterSum = 0;
        int jitterCount = 0;
        double? previous = null;

        foreach (PingSample sample in samples)
        {
            if (sample.RttMs is not double rtt)
            {
                lost++;
                previous = null;
                continue;
            }

            received++;
            sum += rtt;
            min = Math.Min(min, rtt);
            max = Math.Max(max, rtt);

            if (previous is double prev)
            {
                jitterSum += Math.Abs(rtt - prev);
                jitterCount++;
            }

            previous = rtt;
        }

        return new PingStatistics
        {
            Target = target,
            SampleCount = samples.Count,
            LostCount = lost,
            MinMs = received > 0 ? min : null,
            MaxMs = received > 0 ? max : null,
            AverageMs = received > 0 ? sum / received : null,
            JitterMs = jitterCount > 0 ? jitterSum / jitterCount : null,
            WindowStart = samples[0].Timestamp,
            WindowEnd = samples[^1].Timestamp,
        };
    }

    /// <summary>
    /// Umbrales de salud del ping. Deliberadamente generosos en latencia y duros en
    /// perdida: 40 ms a un procesador LED no rompe nada, un 2 % de perdida si.
    /// </summary>
    public static HealthState Evaluate(
        PingStatistics stats,
        double warnRttMs = 40,
        double criticalRttMs = 150,
        double warnLossPercent = 1,
        double criticalLossPercent = 5)
    {
        if (stats.SampleCount == 0)
        {
            return HealthState.Unknown;
        }

        if (stats.LossPercent >= criticalLossPercent || stats.AverageMs is null)
        {
            return HealthState.Critical;
        }

        if (stats.AverageMs >= criticalRttMs)
        {
            return HealthState.Critical;
        }

        if (stats.LossPercent >= warnLossPercent || stats.AverageMs >= warnRttMs)
        {
            return HealthState.Warning;
        }

        return HealthState.Ok;
    }
}
