using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El inventario de red guarda dos cosas que el usuario ha escrito a mano —el alias y la
/// estrella— y las mezcla con lo que devuelve un barrido automático. Que un escaneo pise
/// cualquiera de las dos es el fallo que haría que se dejase de usar la función, así que
/// se prueba aquí y no se confía a un UPDATE escrito a mano en el repositorio.
/// </summary>
public class DeviceInventoryTests
{
    private static readonly DateTimeOffset Ayer = new(2026, 8, 25, 17, 12, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ahora = new(2026, 8, 26, 19, 40, 0, TimeSpan.Zero);

    private static DiscoveredDevice Equipo(
        string ip,
        string? alias = null,
        bool critico = false,
        string? mac = null,
        string? vendor = null,
        DateTimeOffset? primeraVez = null,
        DateTimeOffset? ultimaVez = null) => new()
    {
        Id = ip,
        IpAddress = ip,
        Alias = alias,
        IsCritical = critico,
        MacAddress = mac,
        Vendor = vendor,
        FirstSeen = primeraVez ?? default,
        LastSeen = ultimaVez ?? default,
    };

    // ------------------------------------------------------------------ fusión

    [Fact]
    public void Un_barrido_no_pisa_el_alias_ni_la_estrella()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.50", alias: "Procesador LED", critico: true,
                mac: "00:0E:C6:11:22:33", primeraVez: Ayer, ultimaVez: Ayer),
        };

        // El barrido no sabe nada del alias ni de la estrella: solo trae lo que ve la red.
        DiscoveredDevice[] barrido = { Equipo("192.168.1.50", vendor: "Novastar") };

        DeviceMerge merge = DeviceInventory.Merge(inventario, barrido, Ahora);

        DiscoveredDevice resultado = Assert.Single(merge.Devices);
        Assert.Equal("Procesador LED", resultado.Alias);
        Assert.True(resultado.IsCritical);
        Assert.Empty(merge.Added);
    }

    [Fact]
    public void Un_barrido_si_actualiza_fabricante_mac_y_ultima_vez()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.2", alias: "Switch Dante", primeraVez: Ayer, ultimaVez: Ayer),
        };

        DiscoveredDevice[] barrido =
        {
            Equipo("192.168.1.2", mac: "00:1D:C1:AA:BB:CC", vendor: "Audinate"),
        };

        DiscoveredDevice resultado =
            Assert.Single(DeviceInventory.Merge(inventario, barrido, Ahora).Devices);

        Assert.Equal("00:1D:C1:AA:BB:CC", resultado.MacAddress);
        Assert.Equal("Audinate", resultado.Vendor);
        Assert.Equal(Ahora, resultado.LastSeen);

        // La primera vez que se vio no se toca nunca: es lo que sostiene el «esto no estaba ayer».
        Assert.Equal(Ayer, resultado.FirstSeen);
    }

    [Fact]
    public void Un_dato_que_el_barrido_no_resuelve_no_borra_el_que_ya_habia()
    {
        // No saber hoy la MAC de un equipo no significa que la de ayer fuese mentira.
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.77", mac: "AC:DE:48:00:11:22", vendor: "Epson",
                primeraVez: Ayer, ultimaVez: Ayer),
        };

        DiscoveredDevice[] barrido = { Equipo("192.168.1.77") };

        DiscoveredDevice resultado =
            Assert.Single(DeviceInventory.Merge(inventario, barrido, Ahora).Devices);

        Assert.Equal("AC:DE:48:00:11:22", resultado.MacAddress);
        Assert.Equal("Epson", resultado.Vendor);
    }

    [Fact]
    public void Un_equipo_nunca_visto_entra_como_nuevo_y_sin_estrella()
    {
        DiscoveredDevice[] barrido = { Equipo("192.168.1.31", vendor: "Apple") };

        DeviceMerge merge = DeviceInventory.Merge(Array.Empty<DiscoveredDevice>(), barrido, Ahora);

        DiscoveredDevice nuevo = Assert.Single(merge.Added);
        Assert.Equal(Ahora, nuevo.FirstSeen);
        Assert.Equal(Ahora, nuevo.LastSeen);

        // Si el barrido marcase criticos, escanear la red de un hotel dejaria el ping
        // corriendo contra cuarenta televisores.
        Assert.False(nuevo.IsCritical);
    }

    [Fact]
    public void Un_equipo_que_no_aparece_en_el_barrido_se_queda_como_estaba()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.77", alias: "Proyector sala", primeraVez: Ayer, ultimaVez: Ayer),
        };

        DeviceMerge merge = DeviceInventory.Merge(
            inventario, Array.Empty<DiscoveredDevice>(), Ahora);

        DiscoveredDevice resultado = Assert.Single(merge.Devices);

        // No se borra ni se toca su ultima vez: que no conteste a un barrido no es prueba
        // de nada, y menos en una red con el multicast filtrado.
        Assert.Equal(Ayer, resultado.LastSeen);
        Assert.Empty(merge.Added);
    }

    [Fact]
    public void Un_equipo_anadido_a_mano_y_encontrado_despues_es_el_mismo()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.50", alias: "Procesador LED", critico: true, primeraVez: Ayer),
        };

        DiscoveredDevice[] barrido = { Equipo("192.168.1.50", vendor: "Novastar") };

        DeviceMerge merge = DeviceInventory.Merge(inventario, barrido, Ahora);

        Assert.Single(merge.Devices);
        Assert.Empty(merge.Added);
        Assert.Equal("Novastar", merge.Devices[0].Vendor);
    }

    [Fact]
    public void Las_vias_de_descubrimiento_se_acumulan()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.2", primeraVez: Ayer, ultimaVez: Ayer) with
            {
                SeenBy = DiscoveryMethod.Mdns,
            },
        };

        DiscoveredDevice[] barrido =
        {
            Equipo("192.168.1.2") with { SeenBy = DiscoveryMethod.Arp },
        };

        DiscoveredDevice resultado =
            Assert.Single(DeviceInventory.Merge(inventario, barrido, Ahora).Devices);

        Assert.Equal(DiscoveryMethod.Mdns | DiscoveryMethod.Arp, resultado.SeenBy);
    }

    // ------------------------------------------------------------------ orden

    [Fact]
    public void Las_direcciones_se_ordenan_como_numeros_y_no_como_texto()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.10"),
            Equipo("192.168.1.9"),
            Equipo("192.168.1.100"),
            Equipo("192.168.1.2"),
        };

        IReadOnlyList<DiscoveredDevice> orden = DeviceInventory.Sort(inventario);

        Assert.Equal(
            new[] { "192.168.1.2", "192.168.1.9", "192.168.1.10", "192.168.1.100" },
            orden.Select(d => d.IpAddress));
    }

    [Fact]
    public void Los_criticos_van_primero()
    {
        DiscoveredDevice[] inventario =
        {
            Equipo("192.168.1.2"),
            Equipo("192.168.1.200", critico: true),
        };

        IReadOnlyList<DiscoveredDevice> orden = DeviceInventory.Sort(inventario);

        Assert.Equal("192.168.1.200", orden[0].IpAddress);
    }

    // ------------------------------------------------------------------ altas y bajas

    [Fact]
    public void El_diff_de_criticos_dice_que_entra_y_que_sale()
    {
        CriticalSetChange cambio = DeviceInventory.Diff(
            previous: new[] { "192.168.1.2", "192.168.1.50" },
            current: new[] { "192.168.1.50", "192.168.1.77" });

        Assert.Equal(new[] { "192.168.1.77" }, cambio.Added);
        Assert.Equal(new[] { "192.168.1.2" }, cambio.Removed);
        Assert.False(cambio.IsEmpty);
    }

    [Fact]
    public void Sin_cambios_el_diff_esta_vacio()
    {
        CriticalSetChange cambio = DeviceInventory.Diff(
            previous: new[] { "192.168.1.2" },
            current: new[] { "192.168.1.2" });

        Assert.True(cambio.IsEmpty);
    }

    [Fact]
    public void Quitar_el_ultimo_critico_sale_como_baja_y_no_como_nada()
    {
        // Es el caso que deja la alerta huerfana si el colector no lo ve: el equipo se
        // borra estando en rojo y ya no hay nadie que baje su alerta.
        CriticalSetChange cambio = DeviceInventory.Diff(
            previous: new[] { "192.168.1.50" },
            current: Array.Empty<string>());

        Assert.Equal(new[] { "192.168.1.50" }, cambio.Removed);
    }
}
