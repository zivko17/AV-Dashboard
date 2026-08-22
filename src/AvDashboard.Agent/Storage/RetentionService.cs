using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Retencion y rollup. Sin esto la base crece sin control: un ping por segundo a doce
/// dispositivos son 378 millones de filas al ano.
///
/// Muestras en crudo 7 dias, despues rollup horario. Eventos 90 dias.
/// </summary>
public sealed class RetentionService : BackgroundService
{
    private readonly AgentDatabase _database;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        AgentDatabase database,
        AgentOptions options,
        ILogger<RetentionService> logger)
    {
        _database = database;
        _options = options.Retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primera pasada al minuto de arrancar: si el agente lleva parado una temporada
        // hay trabajo acumulado, pero no queremos competir con el arranque del sistema.
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);

            do
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Parada normal del servicio.
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset rawCutoff = DateTimeOffset.UtcNow.AddDays(-_options.RawSampleDays);
            DateTimeOffset eventCutoff = DateTimeOffset.UtcNow.AddDays(-_options.EventDays);

            int rolled = await RollUpPingSamplesAsync(rawCutoff, cancellationToken).ConfigureAwait(false);
            int deleted = await DeleteOldRowsAsync(rawCutoff, eventCutoff, cancellationToken).ConfigureAwait(false);

            if (rolled > 0 || deleted > 0)
            {
                _logger.LogInformation(
                    "Retención: {Rolled} horas agregadas, {Deleted} filas eliminadas.", rolled, deleted);
            }
        }
        catch (SqliteException ex)
        {
            // La retencion nunca es motivo para tumbar el agente.
            _logger.LogWarning(ex, "La pasada de retención falló; se reintenta en la siguiente hora.");
        }
    }

    /// <summary>
    /// Agrega en <c>ping_rollup</c> las muestras anteriores al corte y calcula el jitter
    /// con la misma formula que la vista en vivo, para que el historico y el directo
    /// digan lo mismo.
    /// </summary>
    private async Task<int> RollUpPingSamplesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        Dictionary<(string Device, string Hour), List<PingSample>> buckets = new();

        await using (SqliteConnection connection = _database.OpenConnection())
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT device_id, ts, rtt_ms FROM ping_samples
                WHERE ts < $cutoff ORDER BY device_id, ts;
                """;
            command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));

            await using SqliteDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string device = reader.GetString(0);
                DateTimeOffset timestamp = DateTimeOffset.Parse(reader.GetString(1));
                string hour = timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH");

                if (!buckets.TryGetValue((device, hour), out List<PingSample>? bucket))
                {
                    bucket = new List<PingSample>();
                    buckets[(device, hour)] = bucket;
                }

                bucket.Add(new PingSample
                {
                    Timestamp = timestamp,
                    RttMs = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                });
            }
        }

        if (buckets.Count == 0)
        {
            return 0;
        }

        await _database.WriteAsync(async connection =>
        {
            await using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (((string device, string hour), List<PingSample> samples) in buckets)
            {
                PingStatistics stats = PingStatisticsCalculator.Compute(device, samples);

                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ping_rollup (device_id, hour, samples, lost, avg_ms, min_ms, max_ms, jitter_ms)
                    VALUES ($device, $hour, $samples, $lost, $avg, $min, $max, $jitter)
                    ON CONFLICT(device_id, hour) DO UPDATE SET
                        samples = excluded.samples, lost = excluded.lost, avg_ms = excluded.avg_ms,
                        min_ms = excluded.min_ms, max_ms = excluded.max_ms, jitter_ms = excluded.jitter_ms;
                    """;

                command.Parameters.AddWithValue("$device", device);
                command.Parameters.AddWithValue("$hour", hour);
                command.Parameters.AddWithValue("$samples", stats.SampleCount);
                command.Parameters.AddWithValue("$lost", stats.LostCount);
                command.Parameters.AddWithValue("$avg", (object?)stats.AverageMs ?? DBNull.Value);
                command.Parameters.AddWithValue("$min", (object?)stats.MinMs ?? DBNull.Value);
                command.Parameters.AddWithValue("$max", (object?)stats.MaxMs ?? DBNull.Value);
                command.Parameters.AddWithValue("$jitter", (object?)stats.JitterMs ?? DBNull.Value);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return buckets.Count;
    }

    private async Task<int> DeleteOldRowsAsync(
        DateTimeOffset rawCutoff,
        DateTimeOffset eventCutoff,
        CancellationToken cancellationToken)
    {
        int deleted = 0;

        await _database.WriteAsync(async connection =>
        {
            deleted += await DeleteAsync(connection, "ping_samples", "ts", rawCutoff, cancellationToken)
                .ConfigureAwait(false);
            deleted += await DeleteAsync(connection, "metrics", "ts", rawCutoff, cancellationToken)
                .ConfigureAwait(false);
            deleted += await DeleteAsync(connection, "events", "ts", eventCutoff, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return deleted;
    }

    private static async Task<int> DeleteAsync(
        SqliteConnection connection,
        string table,
        string column,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE {column} < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
