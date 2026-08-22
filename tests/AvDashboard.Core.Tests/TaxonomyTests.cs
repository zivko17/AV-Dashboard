using AvDashboard.Core.Taxonomy;
using Xunit;

namespace AvDashboard.Core.Tests;

public class TaxonomyTests
{
    [Theory]
    [InlineData("Audiosrv", Sector.Audio)]
    [InlineData("Dante Discovery", Sector.Audio)]
    [InlineData("NDI Discovery Service", Sector.Video)]
    [InlineData("Tailscale", Sector.Network)]
    [InlineData("wuauserv", Sector.SystemCandidate)]
    [InlineData("RpcSs", Sector.SystemEssential)]
    public void La_semilla_clasifica_los_servicios_conocidos(string name, Sector expected)
    {
        ServiceTaxonomy taxonomy = new();

        Assert.Equal(expected, taxonomy.Classify(name));
    }

    [Fact]
    public void Un_servicio_desconocido_se_queda_sin_clasificar()
    {
        ServiceTaxonomy taxonomy = new();

        Assert.Equal(Sector.Unclassified, taxonomy.Classify("ServicioDeAlgoRaro"));
    }

    [Fact]
    public void La_clasificacion_manual_gana_sobre_la_semilla()
    {
        // Mueves un servicio de sector una vez y se queda ahí para siempre.
        ServiceTaxonomy taxonomy = new();
        taxonomy.Assign("Audiosrv", Sector.SystemEssential);

        Assert.Equal(Sector.SystemEssential, taxonomy.Classify("Audiosrv"));
        Assert.Contains("Audiosrv", taxonomy.Overrides.Keys);
    }

    [Fact]
    public void Adivina_por_el_nombre_visible_cuando_no_hay_semilla()
    {
        ServiceTaxonomy taxonomy = new();

        Assert.Equal(Sector.Audio, taxonomy.Classify("XYZ_Svc", "Dante Fabricante Audio Service"));
        Assert.Equal(Sector.Video, taxonomy.Classify("ABC_Svc", "Blackmagic Algo Helper"));
    }

    [Fact]
    public void El_catalogo_de_puertos_resuelve_los_rangos()
    {
        Assert.Contains(AvPorts.Lookup(8703, PortProtocol.Udp), p => p.Service == "Dante audio");
        Assert.Contains(AvPorts.Lookup(5568, PortProtocol.Udp), p => p.Service.StartsWith("sACN"));
        Assert.Empty(AvPorts.Lookup(12345));
    }

    [Fact]
    public void Todos_los_sectores_tienen_etiqueta_y_orden_de_presentacion()
    {
        foreach (Sector sector in Enum.GetValues<Sector>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SectorNames.Display(sector)));
            Assert.Contains(sector, SectorNames.DisplayOrder);
        }
    }
}
