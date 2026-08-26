using AvDashboard.Core.Networking;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Qué descargas del adaptador de red se apagan en un rig de AV.
///
/// Es la lista que distingue esta aplicación de un debloater, y sobre todo es la lista que
/// tienen que compartir la lectura y la escritura. Escrita dos veces se separa a la primera
/// vez que alguien añada una descarga a una sola de ellas, y entonces la fila dice «hay tres
/// adaptadores mal» mientras el botón de arreglarlo deja uno sin tocar.
/// </summary>
public class NicOffloadPolicyTests
{
    private static NicAdvancedProperty Prop(string keyword, string? value, string adapter = "Ethernet") =>
        new(adapter, keyword, value);

    [Fact]
    public void Las_variantes_del_mismo_offload_se_reconocen_por_prefijo()
    {
        // Cada fabricante pone su sufijo: LsoV2IPv4 y LsoV2IPv6 son las dos caras de Large
        // Send Offload, y comparando el nombre completo no se reconoceria ninguna.
        Assert.NotNull(NicOffloadPolicy.Match("*LsoV2IPv4"));
        Assert.NotNull(NicOffloadPolicy.Match("*LsoV2IPv6"));
        Assert.NotNull(NicOffloadPolicy.Match("*InterruptModeration"));
    }

    [Fact]
    public void Lo_que_no_esta_en_la_lista_no_se_toca()
    {
        // Es lo que separa esto de un debloater: no se apaga todo lo que suene a ahorro.
        Assert.Null(NicOffloadPolicy.Match("*JumboPacket"));
        Assert.Null(NicOffloadPolicy.Match("*SpeedDuplex"));
        Assert.Null(NicOffloadPolicy.Match("VlanId"));
        Assert.Null(NicOffloadPolicy.Match(null));
    }

    [Fact]
    public void Una_descarga_ya_apagada_no_es_infractora()
    {
        Assert.False(NicOffloadPolicy.IsOffending(Prop("*LsoV2IPv4", "0")));
        Assert.True(NicOffloadPolicy.IsOffending(Prop("*LsoV2IPv4", "1")));
    }

    [Fact]
    public void Un_valor_que_no_se_ha_podido_leer_no_cuenta_como_infractor()
    {
        // No saber no es lo mismo que estar mal. Pintar en rojo lo que no se ha leido llena
        // la pantalla de falsos positivos hasta que dejas de mirar los verdaderos.
        Assert.False(NicOffloadPolicy.IsOffending(Prop("*EEE", null)));
    }

    [Fact]
    public void El_plan_agrupa_por_adaptador_y_no_por_propiedad()
    {
        // Ensenar «siete propiedades» no le dice nada a nadie; «Ethernet, Ethernet 2» si,
        // porque es donde hay que ir a mirar.
        NicOffloadPlan plan = NicOffloadPolicy.Plan(new[]
        {
            Prop("*LsoV2IPv4", "1", "Ethernet"),
            Prop("*LsoV2IPv6", "1", "Ethernet"),
            Prop("*InterruptModeration", "1", "Ethernet"),
            Prop("*EEE", "1", "Ethernet 2"),
            Prop("*JumboPacket", "9014", "Ethernet 2"),
        });

        Assert.Equal(4, plan.Offending.Count);
        Assert.Equal(new[] { "Ethernet", "Ethernet 2" }, plan.Adapters);
        Assert.Equal("Ethernet, Ethernet 2", plan.Describe());
        Assert.False(plan.IsClean);
    }

    [Fact]
    public void Una_maquina_limpia_lo_dice_asi()
    {
        NicOffloadPlan plan = NicOffloadPolicy.Plan(new[]
        {
            Prop("*LsoV2IPv4", "0"),
            Prop("*InterruptModeration", "0"),
            Prop("*JumboPacket", "9014"),
        });

        Assert.True(plan.IsClean);
        Assert.Equal("ninguno", plan.Describe());
        Assert.Empty(plan.Adapters);
    }

    [Fact]
    public void Con_muchos_adaptadores_la_fila_se_corta_pero_avisa()
    {
        NicOffloadPlan plan = NicOffloadPolicy.Plan(new[]
        {
            Prop("*EEE", "1", "NIC 1"),
            Prop("*EEE", "1", "NIC 2"),
            Prop("*EEE", "1", "NIC 3"),
            Prop("*EEE", "1", "NIC 4"),
        });

        Assert.EndsWith("…", plan.Describe(), StringComparison.Ordinal);
        Assert.Equal(4, plan.Adapters.Count);
    }

    [Fact]
    public void Cada_regla_explica_por_que_se_apaga()
    {
        // Es lo que se ensena cuando alguien pregunta por que la aplicacion quiere tocar el
        // driver de red. Una regla sin motivo es una regla que nadie va a poder defender.
        Assert.All(NicOffloadPolicy.Rules, rule =>
        {
            Assert.StartsWith("*", rule.Keyword, StringComparison.Ordinal);
            Assert.True(rule.Reason.Length > 40, $"«{rule.Keyword}» no explica nada");
        });
    }
}
