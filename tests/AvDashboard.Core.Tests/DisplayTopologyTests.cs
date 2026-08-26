using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Cómo se escribe una salida de vídeo cuando el monitor no dice su nombre.
///
/// Salió de una captura del panel: la tarjeta de Vídeo mostraba el nombre de dispositivo de
/// Windows en crudo. No le dice nada a nadie y parece un error de la aplicación.
/// </summary>
public class DisplayTopologyTests
{
    /// <summary>
    /// El prefijo de dispositivo, montado a partir de sus caracteres.
    ///
    /// Escrito como literal se pierde una barra en cuanto el fichero pasa por cualquier
    /// herramienta que interprete escapes, y la prueba deja de probar lo que dice.
    /// </summary>
    private const string Prefix = @"\\.\";

    private static DisplayOutput Salida(string device, string? monitor = null) => new()
    {
        DeviceName = device,
        MonitorName = monitor,
    };

    [Fact]
    public void Con_nombre_de_monitor_se_lee_el_nombre()
    {
        Assert.Equal("MSI MP243X", Salida(Prefix + "DISPLAY1", "MSI MP243X").DisplayLabel);
    }

    [Fact]
    public void Sin_nombre_se_lee_el_numero_de_salida_y_no_la_ruta_del_dispositivo()
    {
        Assert.Equal("Salida 145", Salida(Prefix + "DISPLAY145").DisplayLabel);
        Assert.Equal("Salida 1", Salida(Prefix + "DISPLAY1").DisplayLabel);
    }

    [Fact]
    public void Un_nombre_vacio_cuenta_como_sin_nombre()
    {
        // El driver a veces devuelve cadena vacia en vez de null, y una tarjeta con el hueco
        // en blanco se lee como que la aplicacion ha perdido el dato.
        Assert.Equal("Salida 2", Salida(Prefix + "DISPLAY2", string.Empty).DisplayLabel);
    }

    [Fact]
    public void Un_dispositivo_con_otra_forma_se_ensena_tal_cual()
    {
        // No se inventa un formato para algo que no se reconoce: se ensena lo que hay, que
        // al menos es rastreable hasta el sistema.
        Assert.Equal("RAROBJ", Salida("RAROBJ").DisplayLabel);
        Assert.Equal("COSA", Salida(Prefix + "COSA").DisplayLabel);
    }

    [Fact]
    public void El_prefijo_de_la_prueba_es_el_que_usa_Windows()
    {
        // Guardarrail de la propia prueba: si el literal se estropea, esto salta antes de
        // que las demas empiecen a pasar por el motivo equivocado.
        Assert.Equal(4, Prefix.Length);
        Assert.Equal(@"\\.\", Prefix);
    }
}
