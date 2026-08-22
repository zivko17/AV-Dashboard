using AvDashboard.Core.Profiles;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Perfiles guardados en el nodo (M10).
///
/// Los de <see cref="ProfileLibrary"/> vienen compilados y no se pueden borrar: son el
/// punto de partida. Aquí viven los que llegan importados de un fichero o los que edites
/// en la consola, que sí son tuyos.
///
/// Los pasos se guardan como JSON en una columna en vez de en una tabla aparte. Un perfil
/// se lee y se escribe entero, nunca se consulta «todos los pasos que tocan el plan de
/// energía», así que normalizarlo sería trabajo sin beneficio.
/// </summary>
public sealed class ProfileRepository
{
    private readonly AgentDatabase _database;

    public ProfileRepository(AgentDatabase database)
    {
        _database = database;
    }

    public Task SaveAsync(ProfileDefinition profile, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO profiles (id, name, actions_json, created_at, updated_at)
                VALUES ($id, $name, $steps, $created, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    name         = excluded.name,
                    actions_json = excluded.actions_json,
                    updated_at   = excluded.updated_at;
                """;

            command.Parameters.AddWithValue("$id", profile.Id);
            command.Parameters.AddWithValue("$name", profile.Name);
            command.Parameters.AddWithValue("$steps", ProfileSerializer.Export(profile));
            command.Parameters.AddWithValue("$created", profile.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM profiles WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<IReadOnlyList<ProfileDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT actions_json FROM profiles ORDER BY name;";

        List<ProfileDefinition> profiles = new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ProfileImportResult parsed = ProfileSerializer.Import(reader.GetString(0));

            // Una fila que ya no se puede leer —porque se guardo con un formato posterior—
            // se salta en vez de tumbar la lista entera.
            if (parsed.Profile is ProfileDefinition profile)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    /// <summary>Un perfil por identificador, sea de los compilados o de los guardados.</summary>
    public async Task<ProfileDefinition?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        ProfileDefinition? builtIn = ProfileLibrary.All
            .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

        if (builtIn is not null)
        {
            return builtIn;
        }

        IReadOnlyList<ProfileDefinition> stored = await ListAsync(cancellationToken).ConfigureAwait(false);
        return stored.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
    }
}
