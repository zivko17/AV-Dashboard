using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El ping continuo es el módulo que más veces salva un evento. Sus números no pueden
/// salir de una fórmula improvisada, así que están aquí.
/// </summary>
public class PingStatisticsTests
{
    private static PingSample At(int second, double? rtt) => new()
    {
        Timestamp = new DateTimeOffset(2026, 8, 22, 21, 0, second, TimeSpan.Zero),
        RttMs = rtt,
    };

    [Fact]
    public void Una_ventana_vacia_no_inventa_estadisticas()
    {
        PingStatistics stats = PingStatisticsCalculator.Compute("vx600", Array.Empty<PingSample>());

        Assert.Equal(0, stats.SampleCount);
        Assert.Null(stats.AverageMs);
        Assert.Null(stats.JitterMs);
        Assert.Equal(0, stats.LossPercent);
        Assert.Equal(HealthState.Unknown, PingStatisticsCalculator.Evaluate(stats));
    }

    [Fact]
    public void Calcula_media_minimo_y_maximo_sobre_las_muestras_recibidas()
    {
        PingSample[] samples = { At(0, 2), At(1, 4), At(2, 6) };

        PingStatistics stats = PingStatisticsCalculator.Compute("xilica", samples);

        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(4, stats.AverageMs);
        Assert.Equal(2, stats.MinMs);
        Assert.Equal(6, stats.MaxMs);
    }

    [Fact]
    public void La_perdida_no_entra_en_la_media()
    {
        PingSample[] samples = { At(0, 10), At(1, null), At(2, 20) };

        PingStatistics stats = PingStatisticsCalculator.Compute("vx600", samples);

        Assert.Equal(1, stats.LostCount);
        Assert.Equal(15, stats.AverageMs);
        Assert.Equal(100.0 / 3, stats.LossPercent, precision: 5);
    }

    [Fact]
    public void El_jitter_es_la_media_de_diferencias_consecutivas()
    {
        // 10 -> 14 -> 12: diferencias de 4 y 2, media 3.
        PingSample[] samples = { At(0, 10), At(1, 14), At(2, 12) };

        PingStatistics stats = PingStatisticsCalculator.Compute("vx600", samples);

        Assert.Equal(3, stats.JitterMs);
    }

    [Fact]
    public void Una_perdida_rompe_la_cadena_de_jitter_en_vez_de_contar_como_salto()
    {
        // Sin romper la cadena, el salto de 5 a 200 contaría como jitter enorme y
        // taparía el hecho de que lo que hay en medio es una pérdida.
        PingSample[] samples = { At(0, 5), At(1, null), At(2, 200), At(3, 202) };

        PingStatistics stats = PingStatisticsCalculator.Compute("vx600", samples);

        Assert.Equal(2, stats.JitterMs);
    }

    [Theory]
    [InlineData(2, 0, HealthState.Ok)]
    [InlineData(45, 0, HealthState.Warning)]
    [InlineData(200, 0, HealthState.Critical)]
    [InlineData(2, 2, HealthState.Warning)]
    [InlineData(2, 10, HealthState.Critical)]
    public void El_semaforo_es_mas_duro_con_la_perdida_que_con_la_latencia(
        double rttMs, int lostOutOfHundred, HealthState expected)
    {
        List<PingSample> samples = new();
        for (int i = 0; i < 100; i++)
        {
            samples.Add(At(i % 60, i < lostOutOfHundred ? null : rttMs));
        }

        PingStatistics stats = PingStatisticsCalculator.Compute("vx600", samples);

        Assert.Equal(expected, PingStatisticsCalculator.Evaluate(stats));
    }
}
