using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// La columna «visto» del inventario. La regla que prueba este fichero es la misma que
/// gobierna la pestaña Sistema: una fila no puede afirmar más de lo que se sabe. Aquí eso
/// significa que un equipo que no vigilamos con ping nunca diga cuánto lleva sin verse, y
/// que un equipo recién añadido no salga en rojo antes de haberlo medido.
/// </summary>
public class DeviceSeenStateTests
{
    private static readonly DateTimeOffset Barrido = new(2026, 8, 26, 17, 12, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ahora = new(2026, 8, 26, 19, 40, 0, TimeSpan.Zero);

    private static DiscoveredDevice Equipo(
        bool critico = false,
        DateTimeOffset? primeraVez = null,
        DateTimeOffset? ultimaVez = null) => new()
    {
        Id = "192.168.1.50",
        IpAddress = "192.168.1.50",
        IsCritical = critico,
        FirstSeen = primeraVez ?? default,
        LastSeen = ultimaVez ?? default,
    };

    private static PingStatistics Ping(int muestras, int perdidas) => new()
    {
        Target = "192.168.1.50",
        SampleCount = muestras,
        LostCount = perdidas,
        AverageMs = perdidas < muestras ? 2.1 : null,
    };

    [Fact]
    public void Un_critico_recien_anadido_esta_midiendo_y_no_en_rojo()
    {
        // Es el fallo que tenia la version de cuatro estados: el colector se calla hasta
        // tener diez muestras, pero la fila pintaba «no responde» desde el primer segundo a
        // un equipo que estaba perfectamente.
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(critico: true, primeraVez: Ahora), Ping(muestras: 3, perdidas: 3), Barrido);

        Assert.Equal(DeviceSeenState.Measuring, estado);
    }

    [Fact]
    public void Un_critico_sin_estadisticas_todavia_esta_midiendo()
    {
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(critico: true, primeraVez: Ahora), ping: null, lastScanAt: Barrido);

        Assert.Equal(DeviceSeenState.Measuring, estado);
    }

    [Fact]
    public void Un_critico_que_contesta_responde()
    {
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(critico: true, primeraVez: Barrido, ultimaVez: Ahora),
            Ping(muestras: 900, perdidas: 4),
            Barrido);

        Assert.Equal(DeviceSeenState.Responding, estado);
    }

    [Fact]
    public void Un_critico_que_contestaba_y_ya_no_ha_dejado_de_responder()
    {
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(critico: true, primeraVez: Barrido, ultimaVez: Barrido),
            Ping(muestras: 900, perdidas: 900),
            Barrido);

        Assert.Equal(DeviceSeenState.WentQuiet, estado);
    }

    [Fact]
    public void Un_critico_que_no_contesto_nunca_no_es_lo_mismo_que_uno_caido()
    {
        // Casi siempre es una IP mal escrita. Decirle «sin responder desde las 18:40» manda
        // a mirar el equipo cuando lo que hay que mirar es lo que se tecleo.
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(critico: true, primeraVez: Ahora),
            Ping(muestras: 900, perdidas: 900),
            Barrido);

        Assert.Equal(DeviceSeenState.NeverAnswered, estado);
    }

    [Fact]
    public void Un_no_critico_del_ultimo_barrido_dice_eso_y_nada_mas()
    {
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(primeraVez: Barrido, ultimaVez: Barrido), ping: null, lastScanAt: Barrido);

        Assert.Equal(DeviceSeenState.SeenInLastScan, estado);
    }

    [Fact]
    public void Un_no_critico_que_no_salio_en_el_ultimo_barrido_se_queda_en_su_fecha()
    {
        DateTimeOffset anteayer = Barrido.AddDays(-2);

        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(primeraVez: anteayer, ultimaVez: anteayer), ping: null, lastScanAt: Barrido);

        Assert.Equal(DeviceSeenState.SeenEarlier, estado);
    }

    [Fact]
    public void Un_equipo_a_mano_al_que_se_le_quito_la_estrella_no_finge_haberse_visto()
    {
        // Se anade a mano, nunca contesta, y le quitas la estrella. Ya no se le hace ping,
        // asi que no hay ninguna fecha que ensenar: decir «visto por ultima vez» seria
        // inventarse un dato que no existe.
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(primeraVez: Ahora), ping: null, lastScanAt: Barrido);

        Assert.Equal(DeviceSeenState.NeverSeen, estado);
    }

    [Fact]
    public void Sin_ningun_barrido_hecho_un_no_critico_no_puede_decir_que_salio_en_el_ultimo()
    {
        DeviceSeenState estado = DeviceSeenStates.For(
            Equipo(primeraVez: Barrido, ultimaVez: Barrido), ping: null, lastScanAt: null);

        Assert.Equal(DeviceSeenState.SeenEarlier, estado);
    }

    [Fact]
    public void La_marca_de_nuevo_es_del_dia_y_no_de_las_ultimas_veinticuatro_horas()
    {
        Assert.True(DeviceSeenStates.IsNewToday(Equipo(primeraVez: Barrido), Ahora));
        Assert.False(DeviceSeenStates.IsNewToday(Equipo(primeraVez: Barrido.AddDays(-1)), Ahora));

        // Sin fecha no hay marca: un equipo a mano sin confirmar no es «nuevo de hoy».
        Assert.False(DeviceSeenStates.IsNewToday(Equipo(), Ahora));
    }
}
