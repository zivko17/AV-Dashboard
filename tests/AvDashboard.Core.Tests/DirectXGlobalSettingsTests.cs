using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Las preferencias globales de DirectX, que Windows guarda todas en una sola cadena.
///
/// El formato obliga a que esto exista: escribir un interruptor significa reescribir la
/// cadena entera conservando los otros, y hacerlo a mano es la manera de perder el que no
/// se estaba tocando. La cadena de ejemplo sale de una máquina real.
/// </summary>
public class DirectXGlobalSettingsTests
{
    /// <summary>Tal cual la escribe Windows en un equipo con Watchout instalado.</summary>
    private const string Real = "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;AutoHDREnable=0;";

    [Fact]
    public void Se_lee_cada_interruptor_de_la_cadena()
    {
        Assert.True(DirectXGlobalSettings.Read(Real, DirectXGlobalSettings.SwapEffectUpgrade));
        Assert.False(DirectXGlobalSettings.Read(Real, DirectXGlobalSettings.VariableRefreshRate));
        Assert.False(DirectXGlobalSettings.Read(Real, DirectXGlobalSettings.AutoHdr));
    }

    [Fact]
    public void Un_interruptor_que_no_esta_en_la_cadena_no_es_apagado()
    {
        // Ausente significa que manda el valor de fabrica de Windows, que en los tres es
        // encendido. Devolver false seria el mismo fallo que costo dos filas verdes falsas
        // en la sonda de red.
        Assert.Null(DirectXGlobalSettings.Read("VRROptimizeEnable=0;", DirectXGlobalSettings.AutoHdr));
        Assert.True(DirectXGlobalSettings.Effective("VRROptimizeEnable=0;", DirectXGlobalSettings.AutoHdr));
    }

    [Fact]
    public void Sin_cadena_todos_valen_lo_de_fabrica()
    {
        // Es la maquina donde nadie ha abierto nunca esa pantalla de Configuracion, que es
        // justo donde hace falta que la fila diga «activado» y no «sin leer».
        foreach (string? vacia in new[] { null, "", "   " })
        {
            Assert.True(DirectXGlobalSettings.Effective(vacia, DirectXGlobalSettings.SwapEffectUpgrade));
            Assert.Empty(DirectXGlobalSettings.Parse(vacia));
        }
    }

    [Fact]
    public void Escribir_uno_deja_los_demas_intactos()
    {
        // El motivo de que esta clase exista. Apagar VRR regenerando la cadena de cero se
        // llevaria por delante lo que hubiera al lado.
        string despues = DirectXGlobalSettings.With(
            Real, DirectXGlobalSettings.SwapEffectUpgrade, enabled: false);

        Assert.False(DirectXGlobalSettings.Read(despues, DirectXGlobalSettings.SwapEffectUpgrade));
        Assert.False(DirectXGlobalSettings.Read(despues, DirectXGlobalSettings.VariableRefreshRate));
        Assert.False(DirectXGlobalSettings.Read(despues, DirectXGlobalSettings.AutoHdr));
    }

    [Fact]
    public void Escribir_uno_que_no_estaba_lo_anade_sin_tocar_el_resto()
    {
        string despues = DirectXGlobalSettings.With(
            "VRROptimizeEnable=0;", DirectXGlobalSettings.SwapEffectUpgrade, enabled: false);

        Assert.False(DirectXGlobalSettings.Read(despues, DirectXGlobalSettings.VariableRefreshRate));
        Assert.False(DirectXGlobalSettings.Read(despues, DirectXGlobalSettings.SwapEffectUpgrade));
    }

    [Fact]
    public void El_resultado_tiene_la_forma_que_escribe_Windows()
    {
        // Punto y coma final incluido: si se devuelve sin el, la proxima vez que Windows
        // reescriba la cadena queda un formato mezclado que cuesta leer en un diff.
        string despues = DirectXGlobalSettings.With(
            "", DirectXGlobalSettings.VariableRefreshRate, enabled: false);

        Assert.Equal("VRROptimizeEnable=0;", despues);
    }

    [Fact]
    public void Una_clave_desconocida_no_revienta_ni_se_pierde()
    {
        // La cadena la escribe Windows y puede traer interruptores que esta version no
        // conoce. Ignorarlos al leer esta bien; perderlos al escribir, no.
        string conRareza = "SwapEffectUpgradeEnable=1;AlgoNuevoDeWindows=7;";

        string despues = DirectXGlobalSettings.With(
            conRareza, DirectXGlobalSettings.SwapEffectUpgrade, enabled: false);

        Assert.Contains("AlgoNuevoDeWindows=7", despues, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_trozo_sin_igual_se_ignora_en_vez_de_reventar()
    {
        IReadOnlyList<KeyValuePair<string, string>> pares =
            DirectXGlobalSettings.Parse("basura;VRROptimizeEnable=0;=sinclave;");

        Assert.Single(pares);
        Assert.Equal(DirectXGlobalSettings.VariableRefreshRate, pares[0].Key);
    }

    [Fact]
    public void Un_valor_que_no_es_uno_ni_cero_es_desconocido_y_no_se_adivina()
    {
        Assert.Null(DirectXGlobalSettings.Read("VRROptimizeEnable=7;",
            DirectXGlobalSettings.VariableRefreshRate));
    }
}
