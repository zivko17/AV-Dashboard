using AvDashboard.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Biblioteca persistente de EDID de referencia (M18).
///
/// Guardar el EDID de una salida el día que todo funcionaba, con un nombre que reconozcas,
/// y comparar contra él a partir de entonces. Es la detección temprana de «alguien ha
/// cambiado el splitter»: el procesador negocia distinto, la imagen se ve, y no te enteras
/// hasta que algo no encaja tres horas después.
/// </summary>
public sealed class EdidReferenceRepository
{
    private readonly AgentDatabase _database;

    public EdidReferenceRepository(AgentDatabase database)
    {
        _database = database;
    }

    public Task SaveAsync(
        string connectorKey,
        string name,
        string fingerprint,
        byte[] bytes,
        string? deviceName,
        CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();

            // Una referencia por conector: guardar una nueva sustituye a la anterior, que es
            // lo que quieres cuando has cambiado el cableado a proposito.
            command.CommandText = """
                INSERT INTO edid_references
                    (connector_key, name, fingerprint, bytes, device_name, saved_at)
                VALUES ($key, $name, $fingerprint, $bytes, $device, $ts)
                ON CONFLICT(connector_key) DO UPDATE SET
                    name        = excluded.name,
                    fingerprint = excluded.fingerprint,
                    bytes       = excluded.bytes,
                    device_name = excluded.device_name,
                    saved_at    = excluded.saved_at;
                """;

            command.Parameters.AddWithValue("$key", connectorKey);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$bytes", bytes);
            command.Parameters.AddWithValue("$device", (object?)deviceName ?? DBNull.Value);
            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task DeleteAsync(string connectorKey, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM edid_references WHERE connector_key = $key;";
            command.Parameters.AddWithValue("$key", connectorKey);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<IReadOnlyList<EdidReference>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT connector_key, name, fingerprint, length(bytes), device_name, saved_at
            FROM edid_references
            ORDER BY name;
            """;

        List<EdidReference> references = new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            references.Add(new EdidReference
            {
                ConnectorKey = reader.GetString(0),
                Name = reader.GetString(1),
                Fingerprint = reader.GetString(2),
                ByteCount = reader.GetInt32(3),
                DeviceName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SavedAt = DateTimeOffset.Parse(reader.GetString(5)),
            });
        }

        return references;
    }

    /// <summary>Bytes crudos de una referencia, para poder compararla contra lo que hay ahora.</summary>
    public async Task<byte[]?> GetBytesAsync(string connectorKey, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT bytes FROM edid_references WHERE connector_key = $key;";
        command.Parameters.AddWithValue("$key", connectorKey);

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value as byte[];
    }
}
