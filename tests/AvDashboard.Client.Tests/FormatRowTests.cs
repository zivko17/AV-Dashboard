using AvDashboard.Client.ViewModels;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using Xunit;

namespace AvDashboard.Client.Tests;

/// <summary>
/// Las filas que existen solo para dar formato.
///
/// Viven en Client porque las usan las dos interfaces: en WinUI los enlaces no tienen
/// <c>StringFormat</c>, así que escribir el formato en el XAML significaría escribirlo dos
/// veces y verlas divergir. Ese es su motivo de existir, y esta prueba es lo que hace que
/// merezca la pena: sin ella el formato está en un solo sitio pero sigue sin comprobarse.
/// </summary>
public class FormatRowTests
{
    // ------------------------------------------------------------------ interfaces de red

    private static NetworkInterfaceInfo Nic(
        long linkSpeedBps = 1_000_000_000,
        bool? powerManagement = null,
        params string[] ipv4) => new()
    {
        Id = "{guid}",
        Name = "Ethernet",
        LinkSpeedBps = linkSpeedBps,
        PowerManagementEnabled = powerManagement,
        Ipv4Addresses = ipv4,
        IsUp = true,
    };

    [Fact]
    public void Un_gigabit_que_negocia_a_cien_se_ve()
    {
        // Es la averia silenciosa que mas veces explica un NDI a tirones, y en bits crudos
        // no la ve nadie.
        Assert.Equal("1 Gb/s", new NetworkInterfaceRow(Nic(1_000_000_000)).LinkSpeedText);
        Assert.Equal("100 Mb/s", new NetworkInterfaceRow(Nic(100_000_000)).LinkSpeedText);
        Assert.Equal("2,5 Gb/s", new NetworkInterfaceRow(Nic(2_500_000_000)).LinkSpeedText);
    }

    [Fact]
    public void Sin_enlace_se_dice_y_no_se_ensena_un_cero()
    {
        Assert.Equal("sin enlace", new NetworkInterfaceRow(Nic(0)).LinkSpeedText);
    }

    [Fact]
    public void Una_interfaz_sin_ipv4_lo_dice_en_vez_de_salir_vacia()
    {
        Assert.Equal("sin IPv4", new NetworkInterfaceRow(Nic()).Ipv4Text);
        Assert.Equal("192.168.1.30", new NetworkInterfaceRow(Nic(ipv4: "192.168.1.30")).Ipv4Text);
    }

    [Fact]
    public void El_aviso_de_power_management_solo_sale_cuando_consta_que_esta_activo()
    {
        // Null es «no se ha podido leer» y no es lo mismo que false. Avisar de algo que no se
        // ha leido es un falso positivo, y los falsos positivos hacen que se deje de mirar la
        // pantalla.
        Assert.False(new NetworkInterfaceRow(Nic(powerManagement: null)).PowerManagementWarning);
        Assert.False(new NetworkInterfaceRow(Nic(powerManagement: false)).PowerManagementWarning);
        Assert.True(new NetworkInterfaceRow(Nic(powerManagement: true)).PowerManagementWarning);
    }

    // ------------------------------------------------------------------ servicios

    private static ServiceInfo Servicio(
        ServiceState state = ServiceState.Running,
        ServiceStartMode start = ServiceStartMode.Automatic,
        bool expected = true) => new()
    {
        Name = "Audiosrv",
        DisplayName = "Windows Audio",
        State = state,
        StartMode = start,
        Sector = Sector.Audio,
        ExpectedRunning = expected,
    };

    [Fact]
    public void El_estado_de_un_servicio_se_lee_en_castellano()
    {
        Assert.Equal("en marcha", new ServiceRow(Servicio()).StateText);
        Assert.Equal("parado", new ServiceRow(Servicio(ServiceState.Stopped)).StateText);
        Assert.Equal("arrancando", new ServiceRow(Servicio(ServiceState.StartPending)).StateText);
    }

    [Fact]
    public void El_modo_de_arranque_tambien()
    {
        Assert.Equal("automático", new ServiceRow(Servicio()).StartModeText);
        Assert.Equal("automático (retardado)",
            new ServiceRow(Servicio(start: ServiceStartMode.AutomaticDelayed)).StartModeText);
        Assert.Equal("deshabilitado",
            new ServiceRow(Servicio(start: ServiceStartMode.Disabled)).StartModeText);
    }

    [Fact]
    public void Un_estado_que_no_se_conoce_no_se_traduce_a_algo_que_suene_bien()
    {
        // Inventarse «en marcha» para un valor que no se reconoce es peor que decir que no se
        // sabe: uno manda a mirar otra cosa y el otro manda a mirar aqui.
        Assert.Equal("desconocido", new ServiceRow(Servicio((ServiceState)99)).StateText);
        Assert.Equal("desconocido",
            new ServiceRow(Servicio(start: (ServiceStartMode)99)).StartModeText);
    }

    // ------------------------------------------------------------------ alertas

    [Fact]
    public void Una_alerta_activa_se_lee_con_segundos_y_sin_fecha()
    {
        // Una alerta activa es de hoy, asi que la fecha sobra; los segundos son lo que se
        // correlaciona con lo que se vio en pantalla.
        //
        // La hora se construye como local y no como UTC a proposito. La fila usa
        // LocalDateTime, asi que escribiendo la hora en UTC esta prueba pasaria en el CI
        // —que corre en UTC— y fallaria en cualquier maquina con otro huso. Una prueba que
        // depende de donde se ejecuta es peor que no tenerla: falla cuando no toca y se
        // acaba desactivando.
        DateTime local = new(2026, 8, 26, 21, 4, 37, DateTimeKind.Local);

        AlertRow row = new(new Alert
        {
            Id = 1,
            NodeId = "n",
            Module = "M5",
            Sector = Sector.Network,
            Severity = Severity.Error,
            Kind = "ping.critical",
            Message = "Sin respuesta de Procesador LED",
            RaisedAt = new DateTimeOffset(local),
        });

        Assert.Equal("21:04:37", row.TimeText);
        Assert.Equal(Severity.Error, row.Severity);
        Assert.Equal("Sin respuesta de Procesador LED", row.Message);
    }
}
