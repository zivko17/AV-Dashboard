using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Inventario de la red y lista de equipos vigilados (M5, M17).
///
/// Antes los críticos vivían en `appsettings.json`, lo que obligaba a editar un fichero y
/// reiniciar el servicio para añadir uno. Es la clase de fricción que hace que el módulo
/// que más veces te salva esté apagado: nadie edita un JSON a las 19:40 con el cliente
/// detrás. Ver `docs/adr/0008-los-dispositivos-criticos-viven-en-la-base.md`.
///
/// Escribir aquí es estado del agente y no de la máquina, así que también vale en modo
/// portátil: el mismo razonamiento que el baseline de servicios.
/// </summary>
public sealed class DeviceRepository
{
    private readonly AgentDatabase _database;
    private readonly NodeIdentity _identity;

    /// <summary>Marca en `meta` de que los críticos de la configuración ya se migraron.</summary>
    private const string SeededKey = "devices.seeded-from-config";

    /// <summary>Instante del último barrido, en `meta`. Ver <see cref="GetLastScanAsync"/>.</summary>
    private const string LastScanKey = "devices.last-scan";

    public DeviceRepository(AgentDatabase database, NodeIdentity identity)
    {
        _database = database;
        _identity = identity;
    }

    public async Task<IReadOnlyList<DiscoveredDevice>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT ip, mac, alias, vendor, kind, web_port, is_critical,
                   first_seen, last_seen, notes
            FROM devices
            WHERE node_id = $node;
            """;

        command.Parameters.AddWithValue("$node", _identity.Id);

        List<DiscoveredDevice> devices = new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            devices.Add(Read(reader));
        }

        // El orden lo pone Core: críticos primero y la IP como número, no como texto.
        return DeviceInventory.Sort(devices);
    }

    /// <summary>
    /// Solo las direcciones vigiladas. Es lo que pide el colector cada segundo, y por eso
    /// no reconstruye el objeto entero: es un índice parcial y una columna.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListCriticalAddressesAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT ip, alias FROM devices
            WHERE node_id = $node AND is_critical = 1;
            """;

        command.Parameters.AddWithValue("$node", _identity.Id);

        List<string> addresses = new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            addresses.Add(reader.GetString(0));
        }

        return addresses;
    }

    /// <summary>Alias por dirección, para etiquetar lo que el colector publica.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> ListCriticalAliasesAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT ip, alias FROM devices
            WHERE node_id = $node AND is_critical = 1;
            """;

        command.Parameters.AddWithValue("$node", _identity.Id);

        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            aliases[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return aliases;
    }

    public async Task<DiscoveredDevice?> FindAsync(
        string address, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT ip, mac, alias, vendor, kind, web_port, is_critical,
                   first_seen, last_seen, notes
            FROM devices
            WHERE node_id = $node AND ip = $ip;
            """;

        command.Parameters.AddWithValue("$node", _identity.Id);
        command.Parameters.AddWithValue("$ip", address);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <summary>
    /// Guarda un equipo entero. Es el único camino de escritura: alta manual, renombrar,
    /// marcar y desmarcar son todas «así queda este equipo», y tenerlas en un solo sitio es
    /// la misma idea que el conmutador de SettingsWriter.
    /// </summary>
    public Task SaveAsync(DiscoveredDevice device, CancellationToken cancellationToken = default) =>
        SaveManyAsync(new[] { device }, cancellationToken);

    public Task SaveManyAsync(
        IReadOnlyCollection<DiscoveredDevice> devices,
        CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

            foreach (DiscoveredDevice device in devices)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO devices
                        (id, node_id, mac, ip, alias, vendor, kind, web_port,
                         is_critical, first_seen, last_seen, notes)
                    VALUES
                        ($id, $node, $mac, $ip, $alias, $vendor, $kind, $port,
                         $critical, $first, $last, $notes)
                    ON CONFLICT(node_id, ip) DO UPDATE SET
                        mac         = excluded.mac,
                        alias       = excluded.alias,
                        vendor      = excluded.vendor,
                        kind        = excluded.kind,
                        web_port    = excluded.web_port,
                        is_critical = excluded.is_critical,
                        last_seen   = excluded.last_seen,
                        notes       = excluded.notes;
                    """;

                command.Parameters.AddWithValue("$id", device.Id);
                command.Parameters.AddWithValue("$node", _identity.Id);
                command.Parameters.AddWithValue("$mac", (object?)device.MacAddress ?? DBNull.Value);
                command.Parameters.AddWithValue("$ip", device.IpAddress);
                command.Parameters.AddWithValue("$alias", (object?)device.Alias ?? DBNull.Value);
                command.Parameters.AddWithValue("$vendor", (object?)device.Vendor ?? DBNull.Value);
                command.Parameters.AddWithValue("$kind", device.Kind.ToString());
                command.Parameters.AddWithValue("$port", (object?)device.WebPort ?? DBNull.Value);
                command.Parameters.AddWithValue("$critical", device.IsCritical ? 1 : 0);
                command.Parameters.AddWithValue("$first", Stamp(device.FirstSeen));
                command.Parameters.AddWithValue("$last", Stamp(device.LastSeen));
                command.Parameters.AddWithValue("$notes", (object?)device.Notes ?? DBNull.Value);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Refresca solo la última vez que se vio, sin tocar nada más.
    ///
    /// Lo llama el colector cuando vuelca su lote de muestras, no en cada respuesta: una
    /// escritura por segundo y por equipo a SQLite para actualizar una fecha sería pagar
    /// disco a cambio de una precisión que nadie mira.
    /// </summary>
    public Task TouchAsync(
        IReadOnlyCollection<string> addresses,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        if (addresses.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _database.WriteAsync(async connection =>
        {
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

            foreach (string address in addresses)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;

                command.CommandText = """
                    UPDATE devices SET last_seen = $ts
                    WHERE node_id = $node AND ip = $ip;
                    """;

                command.Parameters.AddWithValue("$ts", seenAt.ToString("O"));
                command.Parameters.AddWithValue("$node", _identity.Id);
                command.Parameters.AddWithValue("$ip", address);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task DeleteAsync(string address, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM devices WHERE node_id = $node AND ip = $ip;";
            command.Parameters.AddWithValue("$node", _identity.Id);
            command.Parameters.AddWithValue("$ip", address);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    // --------------------------------------------------------------- migración

    /// <summary>
    /// Sube a la base los críticos que quedasen en la configuración, una sola vez.
    ///
    /// Devuelve cuántos ha migrado, o null si no había nada que hacer. Se marca en `meta`
    /// en lugar de mirar si la tabla está vacía: quien borre todos sus equipos y reinicie
    /// no debe encontrárselos de vuelta.
    /// </summary>
    public async Task<int?> SeedFromConfigurationAsync(
        IReadOnlyCollection<CriticalDeviceOptions> configured,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (await HasSeededAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await MarkSeededAsync(cancellationToken).ConfigureAwait(false);

        if (configured.Count == 0)
        {
            return null;
        }

        List<DiscoveredDevice> devices = configured
            .Select(device => new DiscoveredDevice
            {
                Id = device.Address,
                IpAddress = device.Address,
                Alias = device.Alias,
                IsCritical = true,
                SeenBy = DiscoveryMethod.Manual,
                FirstSeen = now,

                // Sin LastSeen: que estuviese en la configuración no prueba que respondiera.
                // La columna «visto» dirá «midiendo» hasta que conteste, que es la verdad.
            })
            .ToList();

        await SaveManyAsync(devices, cancellationToken).ConfigureAwait(false);
        return devices.Count;
    }

    /// <summary>
    /// Cuándo se hizo el último barrido.
    ///
    /// Lo necesita la columna «visto» para poder decir «visto en el escaneo de las 17:12» en
    /// lugar de insinuar una vigilancia que no existe. Va en `meta` y no en memoria porque
    /// si no, tras reiniciar el agente todas las filas no vigiladas caerían a «visto por
    /// última vez», que es un dato distinto del que se tiene.
    /// </summary>
    public async Task<DateTimeOffset?> GetLastScanAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", LastScanKey);

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is string raw && DateTimeOffset.TryParse(raw, out DateTimeOffset at) ? at : null;
    }

    public Task SetLastScanAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO meta (key, value) VALUES ($key, $ts)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;

            command.Parameters.AddWithValue("$key", LastScanKey);
            command.Parameters.AddWithValue("$ts", at.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<bool> HasSeededAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", SeededKey);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private Task MarkSeededAsync(CancellationToken cancellationToken) =>
        _database.WriteAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO meta (key, value) VALUES ($key, $ts)
                ON CONFLICT(key) DO NOTHING;
                """;

            command.Parameters.AddWithValue("$key", SeededKey);
            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    // --------------------------------------------------------------- lectura

    private static DiscoveredDevice Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        IpAddress = reader.GetString(0),
        MacAddress = reader.IsDBNull(1) ? null : reader.GetString(1),
        Alias = reader.IsDBNull(2) ? null : reader.GetString(2),
        Vendor = reader.IsDBNull(3) ? null : reader.GetString(3),
        Kind = reader.IsDBNull(4) ? DeviceKind.Unknown : ParseKind(reader.GetString(4)),
        WebPort = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        IsCritical = reader.GetInt32(6) != 0,
        FirstSeen = ParseStamp(reader.IsDBNull(7) ? null : reader.GetString(7)),
        LastSeen = ParseStamp(reader.IsDBNull(8) ? null : reader.GetString(8)),
        Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
    };

    private static DeviceKind ParseKind(string raw) =>
        Enum.TryParse(raw, ignoreCase: true, out DeviceKind kind) ? kind : DeviceKind.Unknown;

    /// <summary>
    /// La fecha vacía se guarda como cadena vacía y vuelve como `default`.
    ///
    /// Es lo que distingue «añadido a mano y todavía sin confirmar» de «visto tal día», y
    /// `DeviceSeenStates` se apoya en esa diferencia para no inventarse una fecha.
    /// </summary>
    private static string Stamp(DateTimeOffset value) => value == default ? "" : value.ToString("O");

    private static DateTimeOffset ParseStamp(string? raw) =>
        string.IsNullOrEmpty(raw) ? default : DateTimeOffset.Parse(raw);
}
