using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El degradado de carga del panel. Lo que se prueba aquí es que la escala no es lineal:
/// una CPU al 50 % en un rig de directo va sobrada y tiene que salir verde, mientras que un
/// enlace de red al 50 % ya tiene que estar avisando. Con una regla de tres las dos saldrían
/// del mismo color, y un panel que avisa cuando no pasa nada acaba siendo un panel que nadie
/// mira.
/// </summary>
public class LoadGradeTests
{
    /// <summary>Verde cuando el tono está claramente del lado verde del ámbar.</summary>
    private static bool IsGreen(LoadColor c) => c.G > c.R + 40;

    private static bool IsRed(LoadColor c) => c.R > c.G + 40;

    private static bool IsAmber(LoadColor c) => Math.Abs(c.R - c.G) <= 60 && c.R > 100 && c.G > 80;

    // ------------------------------------------------------------------ no lineal

    [Fact]
    public void Una_cpu_al_cincuenta_por_ciento_esta_verde()
    {
        // El caso que motiva todo esto. Con una escala lineal saldría a medio camino del
        // rojo, y no hay nada que mirar: un rig renderizando va al 40-60 % sin despeinarse.
        Assert.Equal(0, LoadGrade.Severity(LoadMetric.Cpu, 50));
        Assert.True(IsGreen(LoadGrade.ColorFor(LoadMetric.Cpu, 50)));
        Assert.Equal(HealthState.Ok, LoadGrade.HealthFor(LoadMetric.Cpu, 50));
    }

    [Fact]
    public void Un_enlace_de_red_al_cincuenta_por_ciento_ya_ha_empezado_a_moverse()
    {
        // Misma cifra, lectura opuesta. Lo que tumba NDI y Dante no es saturar el enlace,
        // es acercarse: el jitter se dispara mucho antes del 100 %. Al 50 % todavía no es
        // un aviso —el degradado es progresivo, no un semáforo— pero ya no está en reposo,
        // y la CPU con esa misma cifra sí lo está.
        Assert.True(LoadGrade.Severity(LoadMetric.NetworkLink, 50) > 0);
        Assert.Equal(0, LoadGrade.Severity(LoadMetric.Cpu, 50));

        // Al 70 % la diferencia ya se ve: el enlace está en el codo y la CPU aún holgada.
        Assert.Equal(0.5, LoadGrade.Severity(LoadMetric.NetworkLink, 70), 3);
        Assert.True(LoadGrade.Severity(LoadMetric.Cpu, 70) < 0.3);
    }

    [Fact]
    public void Cada_magnitud_tiene_su_propio_codo()
    {
        // Al 80 % la CPU está peor vista que la VRAM, que aguanta más cerca del tope.
        Assert.True(
            LoadGrade.Severity(LoadMetric.Cpu, 80) > LoadGrade.Severity(LoadMetric.VideoMemory, 80));

        // Y el disco lleno avisa antes que la VRAM llena: quedarse sin espacio a mitad de
        // una grabación no tiene arreglo posterior.
        Assert.True(
            LoadGrade.Severity(LoadMetric.DiskSpace, 80) > LoadGrade.Severity(LoadMetric.VideoMemory, 80));
    }

    // ------------------------------------------------------------------ el degradado

    [Fact]
    public void El_color_va_de_verde_a_rojo_pasando_por_ambar()
    {
        Assert.True(IsGreen(LoadGrade.ColorFor(LoadMetric.Cpu, 10)));
        Assert.True(IsAmber(LoadGrade.ColorFor(LoadMetric.Cpu, 82)));
        Assert.True(IsRed(LoadGrade.ColorFor(LoadMetric.Cpu, 98)));
    }

    [Fact]
    public void El_degradado_es_progresivo_y_no_da_saltos()
    {
        // Ni un escalón: entre dos puntos de un porcentaje el color no puede cambiar de
        // golpe, o la tarjeta parpadea entre dos colores con la carga oscilando.
        LoadColor previous = LoadGrade.ColorFor(LoadMetric.Cpu, 0);

        for (int percent = 1; percent <= 100; percent++)
        {
            LoadColor current = LoadGrade.ColorFor(LoadMetric.Cpu, percent);

            Assert.True(
                Math.Abs(current.R - previous.R) <= 20 && Math.Abs(current.G - previous.G) <= 20,
                $"salto brusco de color en el {percent} %: {previous.Hex} → {current.Hex}");

            previous = current;
        }
    }

    [Fact]
    public void Mas_carga_nunca_pinta_mas_verde()
    {
        double previous = 0;

        for (int percent = 0; percent <= 100; percent++)
        {
            double current = LoadGrade.Severity(LoadMetric.Gpu, percent);
            Assert.True(current >= previous, $"la escala baja en el {percent} %");
            previous = current;
        }
    }

    [Fact]
    public void El_codo_cae_exactamente_a_la_mitad_de_la_escala()
    {
        // No es un detalle estético: el codo es el punto en el que la fila pasa de «va bien»
        // a «míralo», y es lo que ata el color al veredicto de salud.
        Assert.Equal(0.5, LoadGrade.Severity(LoadMetric.Cpu, 82), 3);
        Assert.Equal(HealthState.Warning, LoadGrade.HealthFor(LoadMetric.Cpu, 82));
    }

    // ------------------------------------------------------------------ extremos

    [Fact]
    public void Fuera_de_rango_no_revienta_ni_da_colores_raros()
    {
        Assert.Equal(0, LoadGrade.Severity(LoadMetric.Cpu, -20));
        Assert.Equal(1, LoadGrade.Severity(LoadMetric.Cpu, 500));
        Assert.Equal(0, LoadGrade.Severity(LoadMetric.Cpu, double.NaN));

        Assert.True(IsGreen(LoadGrade.ColorFor(LoadMetric.Cpu, -20)));
        Assert.True(IsRed(LoadGrade.ColorFor(LoadMetric.Cpu, 500)));
    }

    [Fact]
    public void Al_limite_es_rojo_pleno_en_todas_las_magnitudes()
    {
        foreach (LoadMetric metric in Enum.GetValues<LoadMetric>())
        {
            Assert.Equal(1, LoadGrade.Severity(metric, 100));
            Assert.Equal(HealthState.Critical, LoadGrade.HealthFor(metric, 100));
            Assert.True(IsRed(LoadGrade.ColorFor(metric, 100)), $"{metric} no llega a rojo");
        }
    }

    [Fact]
    public void En_reposo_es_verde_en_todas_las_magnitudes()
    {
        foreach (LoadMetric metric in Enum.GetValues<LoadMetric>())
        {
            Assert.True(IsGreen(LoadGrade.ColorFor(metric, 0)), $"{metric} no arranca en verde");
        }
    }
}
