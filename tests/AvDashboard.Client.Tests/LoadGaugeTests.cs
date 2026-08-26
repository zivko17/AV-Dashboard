using AvDashboard.Client.ViewModels;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Client.Tests;

/// <summary>
/// El medidor de una tarjeta del panel: el porcentaje, su color y lo que se lee debajo.
///
/// El color lo decide <see cref="LoadGrade"/> en Core y ahí está probado; lo que se prueba
/// aquí es que el medidor no lo estropee por el camino y, sobre todo, lo que hace cuando no
/// hay dato. Un cero en verde afirmaría que va bien algo de lo que no se sabe nada, que es
/// justo lo que la pestaña Sistema tiene prohibido.
/// </summary>
public class LoadGaugeTests
{
    [Fact]
    public void Sin_dato_no_hay_color_ni_cifra_inventada()
    {
        LoadGaugeViewModel gauge = new("GPU", LoadMetric.Gpu, percent: null,
            unavailableReason: "sin contadores de GPU");

        Assert.False(gauge.HasValue);
        Assert.Equal("—", gauge.PercentText);
        Assert.Equal(HealthState.Unknown, gauge.Health);
        Assert.Equal(0, gauge.Fill);

        // Gris, no verde: pintar un cero en verde seria afirmar que va bien algo de lo que
        // no se sabe nada.
        Assert.Equal("#6E7876", gauge.ColorHex);
    }

    [Fact]
    public void Sin_dato_se_lee_el_motivo_y_no_el_detalle()
    {
        // Es el fallo que salio de mirar una captura: la GPU marcaba «2 %» y debajo «sin
        // contadores de GPU en esta maquina» a la vez, porque el pie encadenaba el detalle
        // con el motivo y esta maquina tiene carga de GPU pero no sensor de temperatura.
        // Una fila que se contradice consigo misma es peor que una sin dato.
        LoadGaugeViewModel sinDato = new("GPU", LoadMetric.Gpu, percent: null,
            detail: null, unavailableReason: "sin contadores de GPU");

        Assert.Equal("sin contadores de GPU", sinDato.Footnote);

        LoadGaugeViewModel conDato = new("GPU", LoadMetric.Gpu, percent: 2,
            detail: null, unavailableReason: "sin contadores de GPU");

        Assert.Null(conDato.Footnote);
    }

    [Fact]
    public void Con_dato_manda_el_detalle_aunque_haya_motivo_declarado()
    {
        LoadGaugeViewModel gauge = new("RAM", LoadMetric.Memory, percent: 32,
            detail: "17,9 / 61,7 GB", unavailableReason: "el agente aún no ha leído nada");

        Assert.Equal("17,9 / 61,7 GB", gauge.Footnote);
    }

    [Fact]
    public void La_cifra_se_lee_como_un_porcentaje_redondeado()
    {
        Assert.Equal("32 %", new LoadGaugeViewModel("RAM", LoadMetric.Memory, 32.4).PercentText);
        Assert.Equal("33 %", new LoadGaugeViewModel("RAM", LoadMetric.Memory, 32.6).PercentText);
    }

    [Fact]
    public void El_relleno_se_satura_al_cien_aunque_el_dato_venga_por_encima()
    {
        // Un contador que devuelve 103 % no puede pintar una barra que se sale de su caja.
        LoadGaugeViewModel gauge = new("CPU", LoadMetric.Cpu, 103);

        Assert.Equal(1, gauge.Fill);
        Assert.Equal(103, gauge.Percent);
    }

    [Fact]
    public void Una_cpu_al_cincuenta_sale_verde_y_un_enlace_al_cincuenta_no()
    {
        // La escala no lineal se prueba en Core; aqui se comprueba que el medidor la usa de
        // verdad y no una regla de tres propia.
        LoadGaugeViewModel cpu = new("CPU", LoadMetric.Cpu, 50);
        LoadGaugeViewModel nic = new("Ethernet", LoadMetric.NetworkLink, 50);

        Assert.Equal(HealthState.Ok, cpu.Health);
        Assert.Equal(LoadGrade.ColorFor(LoadMetric.Cpu, 50).Hex, cpu.ColorHex);
        Assert.NotEqual(cpu.ColorHex, nic.ColorHex);
    }

    [Fact]
    public void Un_disco_lleno_llega_a_critico()
    {
        LoadGaugeViewModel disco = new("C:", LoadMetric.DiskSpace, 97);

        Assert.Equal(HealthState.Critical, disco.Health);
    }
}
