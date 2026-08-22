using AvDashboard.Core.Networking;
using Xunit;

namespace AvDashboard.Core.Tests;

public class MacAddressTests
{
    [Theory]
    [InlineData("00:1D:C1:2A:3B:4C", "001DC12A3B4C")]
    [InlineData("00-1d-c1-2a-3b-4c", "001DC12A3B4C")]
    [InlineData("001dc12a3b4c", "001DC12A3B4C")]
    public void Normaliza_los_formatos_que_devuelven_las_apis_de_windows(string input, string expected)
    {
        Assert.Equal(expected, MacAddress.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-una-mac")]
    [InlineData("00:1D:C1:2A:3B")]
    [InlineData("00:1D:C1:2A:3B:4C:5D")]
    public void Rechaza_lo_que_no_es_una_mac(string input)
    {
        Assert.Null(MacAddress.Normalize(input));
    }

    [Fact]
    public void Formatea_con_dos_puntos()
    {
        Assert.Equal("00:1D:C1:2A:3B:4C", MacAddress.Format("001dc12a3b4c"));
    }

    [Fact]
    public void Detecta_las_mac_administradas_localmente()
    {
        // Bit 0x02 del primer octeto: máquinas virtuales y MAC aleatorizadas.
        Assert.True(MacAddress.IsLocallyAdministered("02:00:00:00:00:01"));
        Assert.False(MacAddress.IsLocallyAdministered("00:1D:C1:2A:3B:4C"));
    }
}

public class OuiDatabaseTests
{
    [Fact]
    public void La_semilla_resuelve_los_fabricantes_que_aparecen_en_todos_los_rigs()
    {
        OuiDatabase database = new();

        Assert.Equal("Audinate", database.Lookup("00:1D:C1:2A:3B:4C"));
        Assert.Equal("Blackmagic Design", database.Lookup("7C-2E-0D-11-22-33"));
    }

    [Fact]
    public void Una_mac_desconocida_devuelve_null_en_vez_de_inventarse_un_fabricante()
    {
        // A8 no tiene el bit de administracion local puesto: es una MAC de fabricante
        // real y desconocida, no una virtual, que es un caso distinto.
        OuiDatabase database = new();

        Assert.Null(database.Lookup("A8:BB:CC:DD:EE:FF"));
    }

    [Fact]
    public void Una_mac_administrada_localmente_no_tiene_fabricante_que_buscar()
    {
        OuiDatabase database = new();

        Assert.Null(database.Lookup("02:1D:C1:2A:3B:4C"));
    }

    [Fact]
    public void Carga_el_csv_de_ieee_con_los_tres_tamanos_de_prefijo()
    {
        const string csv = """
            Registry,Assignment,Organization Name,Organization Address
            MA-L,ACCDEF,"Fabricante, S.L.",Palma
            MA-M,1234567,Fabricante Mediano,Madrid
            MA-S,123456789,Fabricante Pequeño,Barcelona
            """;

        OuiDatabase database = new();
        int loaded = database.LoadIeeeCsv(new StringReader(csv));

        Assert.Equal(3, loaded);
        Assert.False(database.IsSeedOnly);
        Assert.Equal("Fabricante, S.L.", database.Lookup("AC:CD:EF:00:11:22"));
    }

    [Fact]
    public void El_prefijo_mas_especifico_gana()
    {
        const string csv = """
            Registry,Assignment,Organization Name,Organization Address
            MA-L,ACCDEF,Genérico,X
            MA-S,ACCDEF123,Específico,X
            """;

        OuiDatabase database = new();
        database.LoadIeeeCsv(new StringReader(csv));

        Assert.Equal("Específico", database.Lookup("AC:CD:EF:12:34:56"));
        Assert.Equal("Genérico", database.Lookup("AC:CD:EF:99:99:99"));
    }

    [Fact]
    public void Se_puede_etiquetar_hardware_propio_a_mano()
    {
        OuiDatabase database = new();

        Assert.True(database.Register("A8-BB-CC", "Rack B"));
        Assert.Equal("Rack B", database.Lookup("A8:BB:CC:01:02:03"));
        Assert.False(database.Register("no-es-un-prefijo", "X"));
    }
}
