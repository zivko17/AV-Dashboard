using AvDashboard.Core.Contracts;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El catálogo lo consumen la paleta Ctrl+K y el CLI. Un comando duplicado o una ruta
/// mal escrita rompe los dos a la vez.
/// </summary>
public class CommandCatalogTests
{
    [Fact]
    public void No_hay_nombres_ni_alias_duplicados()
    {
        List<string> names = CommandCatalog.All
            .SelectMany(c => c.Aliases.Prepend(c.Name))
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Todas_las_rutas_apuntan_a_la_api_versionada()
    {
        Assert.All(CommandCatalog.All, c => Assert.StartsWith(ApiRoutes.Base, c.Route, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("whoison")]
    [InlineData("myip")]
    [InlineData("LATENCY")]
    public void Los_nombres_heredados_de_av_light_tools_siguen_resolviendo(string name)
    {
        Assert.NotNull(CommandCatalog.Find(name));
    }

    [Fact]
    public void La_busqueda_prioriza_el_prefijo_sobre_la_descripcion()
    {
        List<CommandDescriptor> results = CommandCatalog.Search("scan").ToList();

        Assert.Equal("scan", results[0].Name);
    }

    [Fact]
    public void Una_consulta_vacia_devuelve_todo_el_catalogo()
    {
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.Search("  ").Count());
    }
}
