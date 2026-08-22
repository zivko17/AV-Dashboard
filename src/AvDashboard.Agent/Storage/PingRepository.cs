using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Persistencia del ping continuo. Escribe por lotes: un INSERT por muestra con doce
/// dispositivos a un segundo son 43.200 transacciones al dia, y eso se nota en un SSD
/// que ademas esta grabando vídeo.
/// </summary>
public sealed class PingRepository
{
    private readonly AgentDatabase _database;

    public PingRepository(AgentDatabase database)
    {
        _database = database;
    }

    public Task AppendBatchAsync(
        IReadOnlyList<(string DeviceId, PingSample Sample)> batch,
        CancellationToken cancellationToken = default)
    {
        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _database.WriteAsync(async connection =>
        {
            await using SqliteTransaction transaction = connection.BeginTransaction();
            await using SqliteCommand command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ping_samples (device_id, ts, rtt_ms, lost)
                VALUES ($device, $ts, $rtt, $lost);
                """;

            SqliteParameter device = command.Parameters.Add("$device", SqliteType.Text);
            SqliteParameter ts = command.Parameters.Add("$ts", SqliteType.Text);
            SqliteParameter rtt = command.Parameters.Add("$rtt", SqliteType.Real);
            SqliteParameter lost = command.Parameters.Add("$lost", SqliteType.Integer);

            foreach ((string deviceId, PingSample sample) in batch)
            {
                device.Value = deviceId;
                ts.Value = sample.Timestamp.ToString("O");
                rtt.Value = (object?)sample.RttMs ?? DBNull.Value;
                lost.Value = sample.Lost ? 1 : 0;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PingSample>> GetWindowAsync(
        string deviceId,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT ts, rtt_ms FROM ping_samples
            WHERE device_id = $device AND ts >= $from
            ORDER BY ts ASC;
            """;

        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));

        List<PingSample> samples = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            samples.Add(new PingSample
            {
                Timestamp = DateTimeOffset.Parse(reader.GetString(0)),
                RttMs = reader.IsDBNull(1) ? null : reader.GetDouble(1),
            });
        }

        return samples;
    }

    public async Task<PingStatistics> GetStatisticsAsync(
        string deviceId,
        string target,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PingSample> samples = await GetWindowAsync(
            deviceId, DateTimeOffset.UtcNow - window, cancellationToken).ConfigureAwait(false);

        return PingStatisticsCalculator.Compute(target, samples);
    }
}
