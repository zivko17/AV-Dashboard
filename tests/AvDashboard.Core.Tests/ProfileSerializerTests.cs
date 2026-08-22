using AvDashboard.Core.Profiles;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Un perfil exportado se abre en otra máquina, a veces meses después. Lo que se prueba
/// aquí es que el viaje de ida y vuelta no pierde nada y que un fichero malo se rechaza
/// con un mensaje, no con una excepción a mitad de una vista.
/// </summary>
public class ProfileSerializerTests
{
    [Fact]
    public void El_viaje_de_ida_y_vuelta_conserva_el_perfil()
    {
        ProfileDefinition original = ProfileLibrary.EventoRigPortatil;

        string json = ProfileSerializer.Export(original, "PC_ANDRES");
        ProfileImportResult result = ProfileSerializer.Import(json);

        Assert.True(result.Success);
        Assert.Equal(original.Name, result.Profile!.Name);
        Assert.Equal(original.Steps.Count, result.Profile.Steps.Count);
        Assert.Empty(result.UnknownActions);
    }

    [Fact]
    public void Los_pasos_conservan_orden_parametros_y_tolerancia_a_fallo()
    {
        ProfileDefinition original = new()
        {
            Id = "p1",
            Name = "Prueba",
            Steps = new[]
            {
                new ProfileStep
                {
                    ActionId = ActionIds.PowerPlan,
                    ParametersJson = """{"scheme":"ultimate"}""",
                    Label = "Plan de energía → Ultimate",
                    Order = 7,
                    ContinueOnError = true,
                },
            },
        };

        ProfileStep step = ProfileSerializer.Import(ProfileSerializer.Export(original))
            .Profile!.Steps.Single();

        Assert.Equal(ActionIds.PowerPlan, step.ActionId);
        Assert.Equal("""{"scheme":"ultimate"}""", step.ParametersJson);
        Assert.Equal("Plan de energía → Ultimate", step.Label);
        Assert.Equal(7, step.Order);
        Assert.True(step.ContinueOnError);
    }

    [Fact]
    public void El_fichero_lleva_version_de_formato_y_de_donde_salio()
    {
        string json = ProfileSerializer.Export(ProfileLibrary.Oficina, "clap-house");

        Assert.Contains("\"formatVersion\": 1", json);
        Assert.Contains("clap-house", json);
    }

    [Fact]
    public void Un_formato_mas_nuevo_se_rechaza_diciendo_por_que()
    {
        // Un perfil exportado por una version futura puede tener campos que aqui se
        // ignorarian en silencio, y aplicar un perfil a medias es peor que no aplicarlo.
        string json = """
            { "formatVersion": 99, "profile": { "id": "p", "name": "Futuro", "steps": [] } }
            """;

        ProfileImportResult result = ProfileSerializer.Import(json);

        Assert.False(result.Success);
        Assert.Contains("99", result.Error);
        Assert.Contains("Actualiza", result.Error);
    }

    [Fact]
    public void Un_perfil_con_actuadores_desconocidos_se_importa_pero_se_avisa()
    {
        // Es el caso de un perfil que viene de un nodo con hardware que aqui no hay. No es
        // un error, pero hay que decirlo antes y no descubrirlo a mitad de la ejecucion.
        string json = """
            {
              "formatVersion": 1,
              "profile": {
                "id": "p", "name": "De otro rig",
                "steps": [
                  { "actionId": "video.decklink-format", "parametersJson": "{}" },
                  { "actionId": "system.power-plan", "parametersJson": "{}" }
                ]
              }
            }
            """;

        ProfileImportResult result = ProfileSerializer.Import(json);

        Assert.True(result.Success);
        Assert.Equal(new[] { "video.decklink-format" }, result.UnknownActions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_fichero_vacio_se_rechaza(string json)
    {
        Assert.Contains("vacío", ProfileSerializer.Import(json).Error);
    }

    [Fact]
    public void Un_fichero_que_no_es_json_se_rechaza_sin_lanzar()
    {
        ProfileImportResult result = ProfileSerializer.Import("esto no es json {{{");

        Assert.False(result.Success);
        Assert.Contains("no es un perfil válido", result.Error);
    }

    [Fact]
    public void Un_json_valido_que_no_es_un_perfil_se_rechaza()
    {
        ProfileImportResult result = ProfileSerializer.Import("""{ "formatVersion": 1 }""");

        Assert.False(result.Success);
        Assert.Contains("no contiene ningún perfil", result.Error);
    }

    [Fact]
    public void Un_perfil_sin_nombre_se_rechaza()
    {
        // Sin nombre no hay forma de elegirlo en una lista, y el nombre es la unica parte
        // del perfil que escribe una persona.
        string json = """{ "formatVersion": 1, "profile": { "id": "p", "name": "", "steps": [] } }""";

        Assert.Contains("no tiene nombre", ProfileSerializer.Import(json).Error);
    }

    [Fact]
    public void Los_acentos_no_se_escapan_en_el_fichero()
    {
        // Un perfil exportado se edita a mano de vez en cuando; verlo lleno de í al
        // abrirlo con un editor es una sorpresa evitable.
        ProfileDefinition profile = new()
        {
            Id = "p",
            Name = "Configuración de vídeo",
            Steps = Array.Empty<ProfileStep>(),
        };

        Assert.Contains("Configuración de vídeo", ProfileSerializer.Export(profile));
    }
}
