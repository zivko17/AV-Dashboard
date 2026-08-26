using AvDashboard.Client.ViewModels;
using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Client.Tests;

/// <summary>
/// El orden y el recorte de los medidores dentro de una tarjeta del panel.
///
/// Esto existe porque ya se rompió una vez y lo pilló una captura de pantalla, no una
/// prueba: la primera versión ordenaba todos los medidores por porcentaje y dejaba la RAM
/// encima de la CPU cuando marcaba más. Eso significa que la tarjeta se rebaraja sola en
/// cada refresco, y un panel que cambia de sitio sus filas cada cinco segundos no se puede
/// leer de memoria, que es justo como se lee cuando hay prisa.
/// </summary>
public class DashboardCardTests
{
    private static LoadGaugeViewModel Gauge(string label, double percent) =>
        new(label, LoadMetric.Cpu, percent);

    [Fact]
    public void Sin_orden_por_gravedad_se_respeta_el_que_trae()
    {
        // CPU y RAM no son intercambiables: cada tarjeta tiene su titular y no puede
        // cambiarlo porque en este segundo la RAM marque mas.
        (IReadOnlyList<LoadGaugeViewModel> shown, string? more) =
            DashboardViewModel.Rank(new[] { Gauge("CPU", 7), Gauge("RAM", 34) });

        Assert.Equal(new[] { "CPU", "RAM" }, shown.Select(g => g.Label));
        Assert.Null(more);
    }

    [Fact]
    public void Con_orden_por_gravedad_sale_la_peor_primero()
    {
        // Una lista de cosas iguales —tres tarjetas de red, cuatro discos— no tiene orden
        // natural, y ahi si hay que ensenar la peor: la que este saturada y no la primera
        // que enumere Windows.
        (IReadOnlyList<LoadGaugeViewModel> shown, _) = DashboardViewModel.Rank(
            new[] { Gauge("Ethernet", 2), Gauge("Ethernet 2", 88), Gauge("Ethernet 3", 40) },
            bySeverity: true);

        Assert.Equal(new[] { "Ethernet 2", "Ethernet 3", "Ethernet" }, shown.Select(g => g.Label));
    }

    [Fact]
    public void Lo_que_no_cabe_se_cuenta_en_vez_de_desaparecer()
    {
        // Un recorte silencioso se lee como «no hay mas», que es mentira. El tope existe
        // para que la tarjeta no rompa la rejilla, no para esconder discos.
        (IReadOnlyList<LoadGaugeViewModel> shown, string? more) = DashboardViewModel.Rank(
            new[] { Gauge("C:", 90), Gauge("D:", 80), Gauge("E:", 70), Gauge("F:", 60), Gauge("G:", 50) },
            bySeverity: true);

        Assert.Equal(DashboardViewModel.MaxGauges, shown.Count);
        Assert.Equal("y 2 más", more);
    }

    [Fact]
    public void Uno_solo_de_sobra_se_dice_en_singular()
    {
        (_, string? more) = DashboardViewModel.Rank(
            new[] { Gauge("C:", 90), Gauge("D:", 80), Gauge("E:", 70), Gauge("F:", 60) },
            bySeverity: true);

        Assert.Equal("y 1 más", more);
    }

    [Fact]
    public void Un_medidor_sin_dato_no_se_cuela_por_delante_de_uno_medido()
    {
        // Sin dato es -1 al ordenar y no cero: un cero se colaria por delante de un
        // adaptador en reposo, y lo que no se sabe no puede pasar por delante de lo que si.
        LoadGaugeViewModel sinDato = new("Ethernet 3", LoadMetric.NetworkLink, percent: null,
            unavailableReason: "sin leer");

        (IReadOnlyList<LoadGaugeViewModel> shown, _) = DashboardViewModel.Rank(
            new[] { sinDato, Gauge("Ethernet", 0) }, bySeverity: true);

        Assert.Equal("Ethernet", shown[0].Label);
    }

    // ------------------------------------------------------------------ la tarjeta

    [Fact]
    public void Una_tarjeta_sin_medidores_ensena_su_veredicto()
    {
        // Audio y Servicios no tienen porcentaje y no se les inventa uno: un numero falso es
        // peor que ninguno.
        StatusCardViewModel card = new()
        {
            Title = "AUDIO",
            Health = HealthState.Ok,
            Primary = "CABLE Input 44,1k",
        };

        Assert.False(card.HasGauges);
        Assert.Equal("CABLE Input 44,1k", card.Primary);
    }

    [Fact]
    public void Al_pulsar_una_tarjeta_se_navega_a_su_seccion()
    {
        // Todo dato del panel es clicable hacia su origen. Si esto se rompe, el panel pasa
        // de ser un indice a ser un adorno.
        string? navegado = null;

        StatusCardViewModel card = new()
        {
            Title = "RED",
            Health = HealthState.Warning,
            Primary = string.Empty,
            Section = "Red",
            Navigate = section => navegado = section,
            Gauges = new[] { Gauge("Ethernet", 3) },
        };

        Assert.True(card.HasGauges);
        card.ActivateCommand.Execute(null);

        Assert.Equal("Red", navegado);
    }

    [Fact]
    public void Una_tarjeta_sin_seccion_no_navega_a_ninguna_parte()
    {
        bool llamado = false;

        StatusCardViewModel card = new()
        {
            Title = "SIN SECCIÓN",
            Health = HealthState.Unknown,
            Primary = "—",
            Navigate = _ => llamado = true,
        };

        card.ActivateCommand.Execute(null);

        Assert.False(llamado);
    }
}
