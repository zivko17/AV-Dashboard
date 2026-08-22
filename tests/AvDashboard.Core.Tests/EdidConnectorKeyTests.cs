using AvDashboard.Core.Contracts;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// La referencia de EDID se guarda contra el conector y no contra el monitor. Si esta clave
/// se calcula mal, cambiar un splitter no dispara nada y la función entera deja de servir
/// para lo único que sirve.
/// </summary>
public class EdidConnectorKeyTests
{
    [Fact]
    public void Saca_el_token_uid_del_identificador_de_instancia()
    {
        Assert.Equal("UID4353", EdidConnectorKey.FromInstanceId("5&1a2b3c&0&UID4353"));
    }

    [Fact]
    public void El_token_se_normaliza_en_mayusculas()
    {
        Assert.Equal("UID4353", EdidConnectorKey.FromInstanceId("5&1a2b3c&0&uid4353"));
    }

    [Fact]
    public void Dos_monitores_distintos_en_el_mismo_puerto_dan_la_misma_clave()
    {
        // Es justo el caso que hace util la funcion: cambias el splitter, el monitor que se
        // presenta es otro, y la clave sigue siendo el puerto.
        string antes = EdidConnectorKey.FromDevicePath(@"\\?\DISPLAY#MSI3CA5#5&1a2b3c&0&UID4353#{guid}");
        string despues = EdidConnectorKey.FromDevicePath(@"\\?\DISPLAY#DEL4021#5&9f8e7d&0&UID4353#{guid}");

        Assert.Equal(antes, despues);
        Assert.Equal("UID4353", antes);
    }

    [Fact]
    public void El_mismo_monitor_en_otro_puerto_da_una_clave_distinta()
    {
        string hdmi = EdidConnectorKey.FromDevicePath(@"\\?\DISPLAY#MSI3CA5#5&1a2b3c&0&UID4353#{guid}");
        string displayPort = EdidConnectorKey.FromDevicePath(@"\\?\DISPLAY#MSI3CA5#5&1a2b3c&0&UID4354#{guid}");

        Assert.NotEqual(hdmi, displayPort);
    }

    [Fact]
    public void Sin_token_uid_se_cae_al_identificador_entero()
    {
        Assert.Equal("5&1A2B3C&0", EdidConnectorKey.FromInstanceId("5&1a2b3c&0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Una_entrada_vacia_no_produce_clave(string? input)
    {
        Assert.Equal(string.Empty, EdidConnectorKey.FromInstanceId(input));
        Assert.Equal(string.Empty, EdidConnectorKey.FromDevicePath(input));
    }

    [Fact]
    public void Una_ruta_que_no_tiene_la_forma_esperada_no_produce_clave()
    {
        Assert.Equal(string.Empty, EdidConnectorKey.FromDevicePath(@"\\?\DISPLAY#MSI3CA5"));
    }
}
