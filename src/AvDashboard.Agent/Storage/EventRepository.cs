using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>Escritura y consulta del Show Timeline y de las alertas (M11).</summary>
public sealed class EventRepository
{
    private readonly AgentDatabase _database;
    private readonly string _nodeId;

    public EventRepository(AgentDatabase database, NodeIdentity identity)
    {
        _database = database;
        _nodeId = identity.Id;
    }

    public Task AppendAsync(
        string module,
        Sector sector,
        Severity severity,
        string kind,
        string message,
        string? payloadJson = null,
        CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO events (node_id, ts, module, sector, severity, kind, message, payload_json)
                VALUES ($node, $ts, $module, $sector, $severity, $kind, $message, $payload);
                """;

            command.Parameters.AddWithValue("$node", _nodeId);
            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$module", module);
            command.Parameters.AddWithValue("$sector", sector.ToString());
            command.Parameters.AddWithValue("$severity", (int)severity);
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$payload", (object?)payloadJson ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Un evento concreto por su identificador.
    ///
    /// Lo pide deshacer: el cliente manda el id de la linea del Timeline y el agente vuelve
    /// a leerla de la base en vez de fiarse de lo que le hayan mandado. Lo que se deshace
    /// tiene que ser lo que consta escrito, no lo que diga la peticion.
    /// </summary>
    public async Task<TimelineEvent?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT id, ts, module, sector, severity, kind, message, payload_json
            FROM events WHERE id = $id AND node_id = $node;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$node", _nodeId);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    public async Task<IReadOnlyList<TimelineEvent>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<string>? modules = null,
        Severity minimumSeverity = Severity.Debug,
        int limit = 2000,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        string moduleFilter = modules is { Count: > 0 }
            ? " AND module IN (" + string.Join(",", modules.Select((_, i) => "$m" + i)) + ")"
            : string.Empty;

        command.CommandText = $"""
            SELECT id, ts, module, sector, severity, kind, message, payload_json
            FROM events
            WHERE ts >= $from AND ts <= $to AND severity >= $severity{moduleFilter}
            ORDER BY ts DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));
        command.Parameters.AddWithValue("$severity", (int)minimumSeverity);
        command.Parameters.AddWithValue("$limit", limit);

        if (modules is { Count: > 0 })
        {
            for (int i = 0; i < modules.Count; i++)
            {
                command.Parameters.AddWithValue("$m" + i, modules[i]);
            }
        }

        List<TimelineEvent> events = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    /// <summary>
    /// Una fila de `events`. Las dos consultas seleccionan las mismas columnas en el mismo
    /// orden a proposito: con dos constructores separados, anadir una columna a una y no a
    /// la otra no da error, da un dato en blanco donde deberia haber uno.
    /// </summary>
    private TimelineEvent ReadEvent(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        NodeId = _nodeId,
        Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
        Module = reader.GetString(2),
        Sector = Enum.TryParse(reader.GetString(3), out Sector sector) ? sector : Sector.Unclassified,
        Severity = (Severity)reader.GetInt32(4),
        Kind = reader.GetString(5),
        Message = reader.GetString(6),
        PayloadJson = reader.IsDBNull(7) ? null : reader.GetString(7),
    };

    /// <summary>
    /// Levanta una alerta si no hay ya una activa del mismo tipo. La deduplicacion es
    /// importante: un display que parpadea no debe generar cuarenta alertas iguales.
    /// </summary>
    public Task RaiseAlertAsync(
        string module,
        Sector sector,
        Severity severity,
        string kind,
        string message,
        CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO alerts (node_id, raised_at, module, sector, severity, kind, message)
                SELECT $node, $ts, $module, $sector, $severity, $kind, $message
                WHERE NOT EXISTS (
                    SELECT 1 FROM alerts
                    WHERE kind = $kind AND module = $module AND cleared_at IS NULL
                );
                """;

            command.Parameters.AddWithValue("$node", _nodeId);
            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$module", module);
            command.Parameters.AddWithValue("$sector", sector.ToString());
            command.Parameters.AddWithValue("$severity", (int)severity);
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$message", message);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task ClearAlertAsync(string module, string kind, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE alerts SET cleared_at = $ts
                WHERE module = $module AND kind = $kind AND cleared_at IS NULL;
                """;

            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$module", module);
            command.Parameters.AddWithValue("$kind", kind);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<IReadOnlyList<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT id, raised_at, module, sector, severity, kind, message, acknowledged
            FROM alerts WHERE cleared_at IS NULL ORDER BY raised_at DESC;
            """;

        List<Alert> alerts = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            alerts.Add(new Alert
            {
                Id = reader.GetInt64(0),
                NodeId = _nodeId,
                RaisedAt = DateTimeOffset.Parse(reader.GetString(1)),
                Module = reader.GetString(2),
                Sector = Enum.TryParse(reader.GetString(3), out Sector sector) ? sector : Sector.Unclassified,
                Severity = (Severity)reader.GetInt32(4),
                Kind = reader.GetString(5),
                Message = reader.GetString(6),
                Acknowledged = reader.GetInt32(7) != 0,
            });
        }

        return alerts;
    }
}
