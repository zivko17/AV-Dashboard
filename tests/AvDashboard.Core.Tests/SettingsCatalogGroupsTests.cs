using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El orden de los grupos en la pantalla es una lista escrita a mano, aparte del enum.
///
/// Esta clase existe por un fallo concreto: se añadió el grupo «Sincronía de hora» al enum,
/// al catálogo y a la sonda, todo compiló, y las tres filas nuevas no aparecieron en ningún
/// sitio. <c>Evaluate</c> recorre esta lista y no el catálogo, así que una fila cuyo grupo no
/// esté aquí se descarta en silencio. Lo detectó una comprobación contra la máquina, no el
/// compilador; a partir de aquí lo detecta esto.
/// </summary>
public class SettingsCatalogGroupsTests
{
    [Fact]
    public void Ningun_grupo_del_catalogo_se_queda_fuera_del_orden_de_pintado()
    {
        IEnumerable<SettingGroup> huerfanos = SettingsCatalog.All
            .Select(d => d.Group)
            .Distinct()
            .Except(SettingsCatalog.Groups);

        Assert.Empty(huerfanos);
    }

    [Fact]
    public void Ninguna_fila_se_pierde_al_evaluar()
    {
        // La otra cara de lo mismo, por si algun dia Evaluate deja de recorrer los grupos:
        // lo que entra en el catalogo tiene que salir en el informe.
        SettingsAudit informe = SettingsCatalog.Evaluate(Array.Empty<SettingReading>());

        Assert.Equal(SettingsCatalog.All.Count, informe.All.Count());
    }

    [Fact]
    public void El_orden_no_repite_grupos()
    {
        // Un grupo repetido pintaria sus filas dos veces y duplicaria sus recuentos.
        Assert.Equal(SettingsCatalog.Groups.Count, SettingsCatalog.Groups.Distinct().Count());
    }

    [Fact]
    public void Todos_los_identificadores_son_unicos()
    {
        // Evaluate indexa las lecturas por id: dos filas con el mismo id compartirian valor
        // y una de las dos ensenaria el de la otra.
        Assert.Equal(
            SettingsCatalog.All.Count,
            SettingsCatalog.All.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
    }
}
