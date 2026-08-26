using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Cuando una directiva de máquina pisa la preferencia del usuario.
///
/// Existe porque un actuador mintió: escribía la preferencia de Game DVR, la releía, y
/// anunciaba «off → on» en el Timeline mientras la máquina seguía con Game DVR apagado
/// porque una directiva lo prohibía. La clave cambiaba de verdad; el comportamiento no.
/// </summary>
public class PolicyOverrideTests
{
    [Fact]
    public void Una_directiva_que_dice_que_no_gana_siempre()
    {
        // Es lo que hace Windows y es lo que el usuario va a observar.
        Assert.False(PolicyOverride.Effective(userPreference: true, policy: false));
        Assert.False(PolicyOverride.Effective(userPreference: null, policy: false));
        Assert.False(PolicyOverride.Effective(userPreference: false, policy: false));
    }

    [Fact]
    public void El_no_del_usuario_tambien_gana_sobre_una_directiva_permisiva()
    {
        // Una directiva que permite algo no lo enciende: solo deja encenderlo.
        Assert.False(PolicyOverride.Effective(userPreference: false, policy: true));
    }

    [Fact]
    public void Sin_directiva_manda_la_preferencia()
    {
        Assert.True(PolicyOverride.Effective(userPreference: true, policy: null));
        Assert.False(PolicyOverride.Effective(userPreference: false, policy: null));
    }

    [Fact]
    public void Si_no_se_ha_podido_leer_ninguna_de_las_dos_no_se_sabe()
    {
        // Y no se sabe no es «apagado». Es la regla de que una fila no afirma mas de lo que
        // consta.
        Assert.Null(PolicyOverride.Effective(userPreference: null, policy: null));
    }

    [Fact]
    public void Encender_algo_que_una_directiva_prohibe_esta_bloqueado()
    {
        Assert.True(PolicyOverride.IsBlockedByPolicy(policy: false, desiredValue: true));
    }

    [Fact]
    public void Apagarlo_no_esta_bloqueado_aunque_la_directiva_lo_prohiba()
    {
        // Apagar lo que la directiva ya mantiene apagado no cambia el comportamiento, pero
        // tampoco miente: la preferencia queda coherente con lo que hace la maquina.
        Assert.False(PolicyOverride.IsBlockedByPolicy(policy: false, desiredValue: false));
    }

    [Fact]
    public void Sin_directiva_nada_esta_bloqueado()
    {
        Assert.False(PolicyOverride.IsBlockedByPolicy(policy: null, desiredValue: true));
        Assert.False(PolicyOverride.IsBlockedByPolicy(policy: true, desiredValue: true));
    }

    [Fact]
    public void El_mensaje_dice_que_hay_que_quitar_la_directiva()
    {
        // Sin eso, quien lo lea va a volver a pulsar el boton pensando que ha fallado.
        string mensaje = PolicyOverride.BlockedMessage("Game DVR");

        Assert.Contains("Game DVR", mensaje, StringComparison.Ordinal);
        Assert.Contains("directiva", mensaje, StringComparison.Ordinal);
    }
}
