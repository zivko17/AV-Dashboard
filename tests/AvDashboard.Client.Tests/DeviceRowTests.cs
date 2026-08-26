using AvDashboard.Client.ViewModels;
using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using Xunit;

namespace AvDashboard.Client.Tests;

/// <summary>
/// La fila de un equipo del inventario de red.
///
/// El estado lo decide <see cref="DeviceSeenStates"/> en Core y ahí está probado. Lo que se
/// prueba aquí es la traducción a palabras, que es donde la regla se puede romper sin que
/// nadie lo note: un equipo que no vigilamos con ping no puede decir cuánto lleva sin verse,
/// porque no hay barrido automático de fondo y lo único que consta es lo que dijo el último
/// escaneo.
/// </summary>
public class DeviceRowTests
{
    private static readonly DateTimeOffset Barrido = new(2026, 8, 26, 17, 12, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ahora = new(2026, 8, 26, 19, 40, 0, TimeSpan.Zero);

    private static DiscoveredDevice Equipo(
        bool critico = false,
        DateTimeOffset? primeraVez = null,
        DateTimeOffset? ultimaVez = null,
        string? alias = null) => new()
    {
        Id = "192.168.1.50",
        IpAddress = "192.168.1.50",
        Alias = alias,
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

    private static DeviceRow Fila(
        DiscoveredDevice device,
        PingStatistics? ping = null,
        DateTimeOffset? barrido = null) =>
        new(device, ping, barrido ?? Barrido, Ahora, _ => Task.CompletedTask, _ => Task.CompletedTask);

    // ------------------------------------------------------------------ la columna «visto»

    [Fact]
    public void Un_critico_recien_anadido_dice_que_esta_midiendo()
    {
        DeviceRow fila = Fila(Equipo(critico: true, primeraVez: Ahora), Ping(3, 3));

        Assert.Equal("midiendo…", fila.SeenText);

        // Y no en rojo: el colector se calla hasta tener diez muestras, y la fila tiene que
        // callarse con el.
        Assert.Equal(HealthState.Unknown, fila.Health);
    }

    [Fact]
    public void Un_critico_que_contesta_responde_y_sale_en_verde()
    {
        DeviceRow fila = Fila(
            Equipo(critico: true, primeraVez: Barrido, ultimaVez: Ahora), Ping(900, 4));

        Assert.Equal("responde", fila.SeenText);
        Assert.Equal(HealthState.Ok, fila.Health);
    }

    [Fact]
    public void Un_critico_caido_dice_desde_cuando()
    {
        DeviceRow fila = Fila(
            Equipo(critico: true, primeraVez: Barrido, ultimaVez: Barrido), Ping(900, 900));

        Assert.Contains("sin responder desde las", fila.SeenText, StringComparison.Ordinal);
        Assert.Equal(HealthState.Critical, fila.Health);
    }

    [Fact]
    public void Un_critico_que_nunca_contesto_no_dice_desde_cuando()
    {
        // Casi siempre es una IP mal tecleada. Decir «sin responder desde las 18:40» manda a
        // mirar el equipo cuando lo que hay que mirar es lo que se escribio, y ademas no hay
        // ninguna hora que poner.
        DeviceRow fila = Fila(Equipo(critico: true, primeraVez: Ahora), Ping(900, 900));

        Assert.Equal("no responde desde que se añadió", fila.SeenText);
        Assert.Equal(HealthState.Critical, fila.Health);
    }

    [Fact]
    public void Un_equipo_sin_estrella_nunca_dice_cuanto_lleva_sin_verse()
    {
        // La regla del proyecto. No se le hace ping, asi que lo unico que consta es lo que
        // dijo el ultimo barrido.
        DeviceRow fila = Fila(Equipo(primeraVez: Barrido, ultimaVez: Barrido));

        Assert.Contains("visto en el escaneo de las", fila.SeenText, StringComparison.Ordinal);
        Assert.DoesNotContain("sin responder", fila.SeenText, StringComparison.Ordinal);

        // Y nunca en rojo: no se le vigila, asi que no hay nada que declarar sobre el.
        Assert.Equal(HealthState.Unknown, fila.Health);
    }

    [Fact]
    public void Un_equipo_que_no_salio_en_el_ultimo_barrido_se_queda_en_su_fecha()
    {
        DateTimeOffset anteayer = Barrido.AddDays(-2);

        DeviceRow fila = Fila(Equipo(primeraVez: anteayer, ultimaVez: anteayer));

        Assert.Contains("visto por última vez", fila.SeenText, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_equipo_a_mano_sin_confirmar_lo_dice_asi()
    {
        // Se anade a mano, nunca contesta, y le quitas la estrella. Ni ping ni barrido lo han
        // visto nunca, asi que no hay ninguna fecha que ensenar.
        DeviceRow fila = Fila(Equipo(primeraVez: Ahora));

        Assert.Equal("añadido a mano, sin confirmar", fila.SeenText);
    }

    [Fact]
    public void Sin_ningun_barrido_hecho_no_se_afirma_que_salio_en_el_ultimo()
    {
        // Se construye a mano y no con el ayudante: el «barrido ?? Barrido» del ayudante
        // convierte el null en la fecha por defecto y la prueba dejaria de probar nada.
        DeviceRow fila = new(
            Equipo(primeraVez: Barrido, ultimaVez: Barrido),
            ping: null, lastScanAt: null, now: Ahora,
            _ => Task.CompletedTask, _ => Task.CompletedTask);

        // Sin saber cuando fue el ultimo barrido, un equipo no puede afirmar que salio en
        // el: cae a su propia fecha, que es el unico dato que consta. Es lo mismo que dice
        // la prueba de DeviceSeenStates en Core.
        Assert.StartsWith("visto por última vez", fila.SeenText, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ la fila

    [Fact]
    public void La_estrella_dice_si_el_equipo_esta_vigilado()
    {
        Assert.Equal("★", Fila(Equipo(critico: true)).StarGlyph);
        Assert.Equal("☆", Fila(Equipo()).StarGlyph);
    }

    [Fact]
    public void Sin_alias_se_lee_la_direccion_y_con_alias_el_nombre()
    {
        Assert.Equal("192.168.1.50", Fila(Equipo()).DisplayName);
        Assert.Equal("Procesador LED", Fila(Equipo(alias: "Procesador LED")).DisplayName);
    }

    [Fact]
    public void Marcar_la_estrella_cambia_el_estado_y_guarda()
    {
        DeviceRow? guardado = null;
        DeviceRow fila = new(Equipo(), null, Barrido, Ahora,
            row => { guardado = row; return Task.CompletedTask; },
            _ => Task.CompletedTask);

        fila.ToggleCriticalCommand.Execute(null);

        Assert.True(fila.IsCritical);
        Assert.Same(fila, guardado);
    }

    [Fact]
    public void Quitar_un_equipo_llama_a_quien_lo_borra()
    {
        DeviceRow? borrado = null;
        DeviceRow fila = new(Equipo(), null, Barrido, Ahora,
            _ => Task.CompletedTask,
            row => { borrado = row; return Task.CompletedTask; });

        fila.RemoveCommand.Execute(null);

        Assert.Same(fila, borrado);
    }

    [Fact]
    public void La_marca_de_nuevo_es_del_dia_y_no_de_las_ultimas_veinticuatro_horas()
    {
        Assert.True(Fila(Equipo(primeraVez: Barrido)).IsNew);
        Assert.False(Fila(Equipo(primeraVez: Barrido.AddDays(-1))).IsNew);
    }
}
