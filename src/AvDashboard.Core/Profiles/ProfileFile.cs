using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Un perfil escrito a fichero, para llevarlo de una máquina a otra.
///
/// Hasta ahora un perfil vivía solo en el SQLite de su nodo. Llegas a un rig nuevo, o
/// reinstalas Windows, y empiezas de cero. Esto lo arregla, y es lo único de la idea del
/// «config file» de los debloaters que merecía la pena copiar.
///
/// Lleva versión de formato porque un perfil exportado hoy tiene que poder abrirse dentro
/// de dos años, cuando el modelo haya cambiado. Y lleva la lista de acciones que usa por
/// separado del contenido: así, al importar, se puede decir «este perfil usa dos actuadores
/// que esta versión no conoce» antes de aplicarlo, en vez de fallar a mitad.
/// </summary>
public sealed record ProfileFile
{
    /// <summary>Versión del formato del fichero, no de la aplicación.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required ProfileDefinition Profile { get; init; }

    /// <summary>Nodo del que salió, solo como referencia de dónde funcionaba.</summary>
    public string? ExportedFrom { get; init; }

    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;

    public const int CurrentFormatVersion = 1;
}

/// <summary>Resultado de leer un fichero de perfil.</summary>
public sealed record ProfileImportResult
{
    public ProfileDefinition? Profile { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Acciones que el perfil referencia y esta versión no conoce.
    ///
    /// No es un error: un perfil de un nodo con hardware que aquí no hay puede traer pasos
    /// que no aplican. Pero hay que decirlo antes, no descubrirlo a mitad de la ejecución.
    /// </summary>
    public IReadOnlyList<string> UnknownActions { get; init; } = Array.Empty<string>();

    public bool Success => Profile is not null && Error is null;
}

/// <summary>Serialización de perfiles a fichero y vuelta.</summary>
public static class ProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },

        // Un perfil exportado se lee y se edita a mano de vez en cuando. Que no escape los
        // acentos de las etiquetas ahorra una sorpresa al abrirlo con un editor.
        Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Export(ProfileDefinition profile, string? nodeName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return JsonSerializer.Serialize(
            new ProfileFile { Profile = profile, ExportedFrom = nodeName },
            Options);
    }

    /// <summary>
    /// Lee un fichero de perfil. Nunca lanza: un fichero corrupto o de una versión futura
    /// es un mensaje en la interfaz, no una excepción que haya que capturar en cada vista.
    /// </summary>
    public static ProfileImportResult Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProfileImportResult { Error = "El fichero está vacío." };
        }

        ProfileFile? file;

        try
        {
            file = JsonSerializer.Deserialize<ProfileFile>(json, Options);
        }
        catch (JsonException ex)
        {
            return new ProfileImportResult { Error = $"El fichero no es un perfil válido: {ex.Message}" };
        }

        if (file?.Profile is not ProfileDefinition profile)
        {
            return new ProfileImportResult { Error = "El fichero no contiene ningún perfil." };
        }

        if (file.FormatVersion > ProfileFile.CurrentFormatVersion)
        {
            return new ProfileImportResult
            {
                Error = $"El fichero usa el formato {file.FormatVersion} y esta versión entiende " +
                        $"hasta el {ProfileFile.CurrentFormatVersion}. Actualiza AV Dashboard.",
            };
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return new ProfileImportResult { Error = "El perfil no tiene nombre." };
        }

        // Se avisa de los actuadores desconocidos pero se importa igual: un perfil de un
        // nodo con hardware que aqui no hay trae pasos que no aplican, y eso es normal.
        List<string> unknown = profile.Steps
            .Select(s => s.ActionId)
            .Where(id => !ActionIds.All.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ProfileImportResult { Profile = profile, UnknownActions = unknown };
    }
}
