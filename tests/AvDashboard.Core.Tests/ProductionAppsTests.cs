using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Las aplicaciones que un rig está ahí para ejecutar, y cómo deberían correr.
///
/// Es lo que convierte «optimiza Windows» en «optimiza Windows para esto». Sin esta lista,
/// la prioridad de proceso es un ajuste genérico que no se puede auditar: no hay forma de
/// decir si la máquina está bien si no se sabe qué se espera que corra en ella.
/// </summary>
public class ProductionAppsTests
{
    private static RunningProcess Proceso(string imagen, ProcessPriority prioridad) =>
        new(imagen, prioridad);

    [Fact]
    public void Los_nombres_de_imagen_son_los_reales_y_no_los_deducidos()
    {
        // Esta prueba existe por un fallo concreto: la primera version de la lista adivino
        // los nombres y fallo en dos de los tres casos que importaban. Resolume Arena es
        // «Arena.exe» y no «Resolume Arena.exe», y Watchout 7 no tiene ningun
        // «WATCHOUT.exe» —es multiproceso y el que pinta es visualrenderer—.
        //
        // Una lista con nombres inventados no da error: no encuentra nada nunca, y la fila
        // sale verde diciendo «ninguna en marcha» con el rig funcionando.
        Assert.NotNull(ProductionApps.Find("vMix64.exe"));
        Assert.NotNull(ProductionApps.Find("Arena.exe"));
        Assert.NotNull(ProductionApps.Find("visualrenderer.exe"));

        Assert.Null(ProductionApps.Find("Resolume Arena.exe"));
        Assert.Null(ProductionApps.Find("WATCHOUT.exe"));
    }

    [Fact]
    public void De_watchout_el_que_pinta_se_distingue_del_que_manda()
    {
        // Si el que pinta y el que manda compiten por la misma CPU, el que tiene que ganar
        // es el que pinta. Meterlos en el mismo saco perderia esa distincion.
        Assert.Equal(ProductionWorkload.MediaServer,
            ProductionApps.Find("visualrenderer.exe")!.Workload);
        Assert.Equal(ProductionWorkload.ShowControl,
            ProductionApps.Find("Producer.exe")!.Workload);
    }

    [Fact]
    public void El_nombre_de_imagen_no_distingue_mayusculas()
    {
        // Windows no las distingue y el proceso puede venir de cualquier sitio.
        Assert.NotNull(ProductionApps.Find("VMIX64.EXE"));
        Assert.NotNull(ProductionApps.Find("arena.exe"));
    }

    [Fact]
    public void El_objetivo_es_alta_y_nunca_tiempo_real()
    {
        // En Realtime el proceso puede dejar sin CPU al kernel: los drivers de audio y red
        // dejan de atenderse y el resultado es peor que no tocar nada.
        Assert.Equal(ProcessPriority.High, ProductionApps.TargetPriority);
        Assert.NotEqual(ProcessPriority.Realtime, ProductionApps.TargetPriority);
    }

    [Fact]
    public void Una_maquina_en_reposo_no_tiene_ningun_problema()
    {
        // Un rig con vMix instalado y cerrado no tiene nada mal. Decir que si es el falso
        // positivo que hace que se deje de mirar la pantalla.
        ProductionAppAudit audit = ProductionApps.Audit(new[]
        {
            Proceso("explorer.exe", ProcessPriority.Normal),
            Proceso("chrome.exe", ProcessPriority.Normal),
        });

        Assert.True(audit.IsClean);
        Assert.Empty(audit.Running);
        Assert.Equal("ninguna en marcha", audit.Describe());
    }

    [Fact]
    public void Una_aplicacion_de_produccion_en_prioridad_normal_sale_senalada()
    {
        ProductionAppAudit audit = ProductionApps.Audit(new[]
        {
            Proceso("vMix64.exe", ProcessPriority.Normal),
            Proceso("explorer.exe", ProcessPriority.Normal),
        });

        Assert.False(audit.IsClean);
        Assert.Equal("vMix", audit.Describe());
    }

    [Fact]
    public void Por_encima_del_objetivo_tambien_vale()
    {
        // Si alguien la ha subido a mano, no hay nada que arreglar. Lo que se busca es que
        // no se quede por debajo.
        ProductionAppAudit audit = ProductionApps.Audit(new[]
        {
            Proceso("visualrenderer.exe", ProcessPriority.Realtime),
        });

        Assert.True(audit.IsClean);
        Assert.Equal("en prioridad alta", audit.Describe());
    }

    [Fact]
    public void Con_varias_mal_se_nombran_todas_y_sin_repetir()
    {
        ProductionAppAudit audit = ProductionApps.Audit(new[]
        {
            Proceso("vMix64.exe", ProcessPriority.Normal),
            Proceso("Arena.exe", ProcessPriority.BelowNormal),
            Proceso("vMix64.exe", ProcessPriority.Normal),
        });

        string texto = audit.Describe();
        Assert.Contains("vMix", texto, StringComparison.Ordinal);
        Assert.Contains("Resolume Arena", texto, StringComparison.Ordinal);
        Assert.Equal(1, texto.Split(',').Count(t => t.Contains("vMix", StringComparison.Ordinal)));
    }

    [Fact]
    public void Cada_aplicacion_declara_que_le_duele()
    {
        // El motivo es lo que se ensena cuando alguien pregunta por que hay que tocar algo.
        // Una entrada sin motivo es una que nadie va a poder defender delante de un cliente.
        Assert.All(ProductionApps.Known, app =>
        {
            Assert.EndsWith(".exe", app.ImageName, StringComparison.OrdinalIgnoreCase);
            Assert.True(app.Rationale.Length > 60, $"«{app.DisplayName}» no explica nada");
        });
    }

    [Fact]
    public void Los_tres_tipos_de_carga_estan_representados()
    {
        // No son el mismo problema: al mezclador le duele la CPU, al servidor de medios la
        // cadencia, y al de audio la latencia. Si algun dia queda un tipo sin ninguna
        // aplicacion, es que la lista se ha quedado coja.
        foreach (ProductionWorkload carga in Enum.GetValues<ProductionWorkload>())
        {
            Assert.Contains(ProductionApps.Known, a => a.Workload == carga);
        }
    }
}
