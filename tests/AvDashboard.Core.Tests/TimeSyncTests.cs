using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// La sincronía de hora, leída del registro sin arrancar el servicio.
///
/// El FILETIME de ejemplo es el de la máquina de trabajo, y coincide al segundo con el
/// evento 35 del visor: por eso se puede contestar «cuándo fue la última vez» sin depender
/// de que W32Time esté en marcha, que es justo cuando hace falta contestarlo.
/// </summary>
public class TimeSyncTests
{
    /// <summary>LastKnownGoodTime de la máquina: 5 de junio de 2026, 18:34:12 UTC.</summary>
    private const long UltimaBuena = 134251580522162848L;

    private static readonly DateTimeOffset Junio =
        new(2026, 6, 5, 18, 34, 12, TimeSpan.Zero);

    [Fact]
    public void El_FILETIME_del_registro_se_convierte_a_la_fecha_del_visor()
    {
        DateTimeOffset? leida = TimeSync.FromFileTime(UltimaBuena);

        Assert.NotNull(leida);
        Assert.Equal(Junio, leida!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Cero_es_nunca_y_no_es_un_fallo_de_lectura()
    {
        // Distinguir «nunca ha sincronizado» de «no se deja leer» es la misma regla que en
        // el registro: una fila sin valor y sin motivo es un fallo.
        Assert.Null(TimeSync.FromFileTime(0));
        Assert.Equal(TimeSyncFreshness.Never, TimeSync.Rate(null, DateTimeOffset.UnixEpoch));
        Assert.Equal("Nunca ha sincronizado", TimeSync.DescribeAge(null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Un_valor_fuera_de_rango_devuelve_null_en_vez_de_lanzar()
    {
        // Lo escribe Windows, pero lo puede haber tocado cualquiera con un editor de
        // registro, y una sonda que lanza deja la pantalla entera sin datos.
        Assert.Null(TimeSync.FromFileTime(-1));
        Assert.Null(TimeSync.FromFileTime(long.MaxValue));
    }

    [Fact]
    public void Ochenta_y_dos_dias_es_antigua()
    {
        // El caso real de la máquina: el servicio dice que sincroniza con time.windows.com
        // y no lo hace desde hace casi tres meses.
        DateTimeOffset agosto = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSyncFreshness.Stale, TimeSync.Rate(Junio, agosto));
        Assert.Equal("Hace 81 días", TimeSync.DescribeAge(Junio, agosto));
    }

    [Fact]
    public void Dentro_de_la_semana_es_reciente()
    {
        Assert.Equal(TimeSyncFreshness.Fresh, TimeSync.Rate(Junio, Junio.AddDays(6.9)));
        Assert.Equal(TimeSyncFreshness.Stale, TimeSync.Rate(Junio, Junio.AddDays(7.1)));
        Assert.Equal(TimeSync.Target, TimeSync.Canonical(TimeSyncFreshness.Fresh));
    }

    [Fact]
    public void Una_fecha_en_el_futuro_no_se_trata_como_error()
    {
        // Pasa cuando el reloj se adelanta y luego se corrige. Montar un caso aparte para
        // eso seria ruido en la pantalla y no ayudaria a nadie.
        Assert.Equal(TimeSyncFreshness.Fresh, TimeSync.Rate(Junio, Junio.AddHours(-3)));
        Assert.Equal("Hace un momento", TimeSync.DescribeAge(Junio, Junio.AddHours(-3)));
    }

    [Fact]
    public void La_antiguedad_cambia_de_unidad_segun_cuanta_sea()
    {
        Assert.Equal("Hace 20 min", TimeSync.DescribeAge(Junio, Junio.AddMinutes(20)));
        Assert.Equal("Hace 5 h", TimeSync.DescribeAge(Junio, Junio.AddHours(5)));
        Assert.Equal("Hace 3 días", TimeSync.DescribeAge(Junio, Junio.AddDays(3)));

        // Recien sincronizado no puede salir «hace 0 min»: parece un dato sin leer.
        Assert.Equal("Hace 1 min", TimeSync.DescribeAge(Junio, Junio.AddSeconds(4)));
    }

    [Fact]
    public void Las_banderas_del_servidor_se_leen_en_hexadecimal()
    {
        // «time.windows.com,0x9» es lo que hay en la maquina: 0x8 cliente y 0x1 intervalo
        // fijo, o sea que manda SpecialPollInterval y no el algoritmo de sondeo.
        IReadOnlyList<NtpPeer> pares = TimeSync.ParsePeers("time.windows.com,0x9");

        Assert.Single(pares);
        Assert.Equal("time.windows.com", pares[0].Host);
        Assert.True(pares[0].Client);
        Assert.True(pares[0].SpecialInterval);
        Assert.False(pares[0].FallbackOnly);
    }

    [Fact]
    public void Varios_servidores_van_separados_por_espacios()
    {
        IReadOnlyList<NtpPeer> pares = TimeSync.ParsePeers(
            "10.0.0.1,0x8 hora.roa.es,0x2");

        Assert.Equal(2, pares.Count);
        Assert.True(pares[0].Client);
        Assert.True(pares[1].FallbackOnly);
    }

    [Fact]
    public void Un_servidor_sin_banderas_es_valido()
    {
        // Es lo que escribe «w32tm /config /manualpeerlist:10.0.0.1» sin mas. Descartarlo
        // dejaria la fila diciendo «sin servidor configurado» con uno configurado.
        IReadOnlyList<NtpPeer> pares = TimeSync.ParsePeers("10.0.0.1");

        Assert.Single(pares);
        Assert.Equal("10.0.0.1", pares[0].Host);
        Assert.Equal(0u, pares[0].Flags);
    }

    [Fact]
    public void Una_bandera_ilegible_no_tira_el_servidor()
    {
        // Lo que importa de la fila es de donde sale la hora. Perder el servidor por no
        // saber leer sus banderas seria cambiar un dato util por ninguno.
        IReadOnlyList<NtpPeer> pares = TimeSync.ParsePeers("10.0.0.1,basura");

        Assert.Single(pares);
        Assert.Equal("10.0.0.1", pares[0].Host);
        Assert.Equal(0u, pares[0].Flags);
    }

    [Fact]
    public void El_origen_se_describe_segun_el_tipo()
    {
        Assert.Equal("time.windows.com", TimeSync.DescribeSource("NTP", "time.windows.com,0x9"));
        Assert.Equal("El dominio", TimeSync.DescribeSource("NT5DS", ""));
        Assert.Equal("Nada: el reloj va solo", TimeSync.DescribeSource("NoSync", "time.windows.com,0x9"));
        Assert.Equal("NTP sin servidor configurado", TimeSync.DescribeSource("NTP", null));
        Assert.Equal("Sin determinar", TimeSync.DescribeSource(null, null));
    }
}
