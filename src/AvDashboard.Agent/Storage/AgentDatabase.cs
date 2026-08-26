using Microsoft.Data.Sqlite;

namespace AvDashboard.Agent.Storage;

/// <summary>
/// Base de datos local del nodo. SQLite en modo WAL: un solo proceso escribe y las
/// lecturas no lo bloquean, que es exactamente el patron del agente.
///
/// El esquema completo esta en docs/data-model.md. Aqui vive la version ejecutable.
/// </summary>
public sealed class AgentDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public AgentDatabase(string databasePath)
    {
        DatabasePath = databasePath;

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Crea el esquema si no existe. Idempotente: se llama en cada arranque.</summary>
    public void Migrate()
    {
        using SqliteConnection connection = OpenConnection();

        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");
        Execute(connection, "PRAGMA foreign_keys=ON;");

        Execute(connection, Schema);
    }

    /// <summary>
    /// Serializa las escrituras. SQLite aguanta escritores concurrentes con reintentos,
    /// pero un agente que devuelve SQLITE_BUSY durante un show no es aceptable.
    /// </summary>
    public async Task WriteAsync(Func<SqliteConnection, Task> work, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = OpenConnection();
            await work(connection).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeLock.Dispose();
        SqliteConnection.ClearAllPools();
        _disposed = true;
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS nodes (
            id             TEXT PRIMARY KEY,
            hostname       TEXT NOT NULL,
            tailscale_ip   TEXT,
            lan_ip         TEXT,
            agent_version  TEXT NOT NULL,
            last_seen      TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS snapshots (
            id           TEXT PRIMARY KEY,
            node_id      TEXT NOT NULL,
            taken_at     TEXT NOT NULL,
            trigger_kind TEXT NOT NULL,  -- 'trigger' es palabra reservada en SQLite
            payload_json TEXT NOT NULL,
            notes        TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_snapshots_node_taken ON snapshots(node_id, taken_at DESC);

        CREATE TABLE IF NOT EXISTS profiles (
            id           TEXT PRIMARY KEY,
            name         TEXT NOT NULL,
            description  TEXT,
            actions_json TEXT NOT NULL,
            inverse_id   TEXT,
            created_at   TEXT NOT NULL,
            updated_at   TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS devices (
            id          TEXT PRIMARY KEY,
            node_id     TEXT NOT NULL,
            mac         TEXT,
            ip          TEXT NOT NULL,
            alias       TEXT,
            vendor      TEXT,
            kind        TEXT,
            web_port    INTEGER,
            is_critical INTEGER NOT NULL DEFAULT 0,
            first_seen  TEXT NOT NULL,
            last_seen   TEXT NOT NULL,
            notes       TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_devices_critical ON devices(is_critical) WHERE is_critical = 1;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_devices_node_ip ON devices(node_id, ip);

        -- Muestras en crudo. Retencion de 7 dias y despues rollup horario; sin esto la
        -- base crece sin control con un ping por segundo a doce dispositivos.
        CREATE TABLE IF NOT EXISTS ping_samples (
            device_id TEXT NOT NULL,
            ts        TEXT NOT NULL,
            rtt_ms    REAL,
            lost      INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_ping_device_ts ON ping_samples(device_id, ts);

        CREATE TABLE IF NOT EXISTS ping_rollup (
            device_id  TEXT NOT NULL,
            hour       TEXT NOT NULL,
            samples    INTEGER NOT NULL,
            lost       INTEGER NOT NULL,
            avg_ms     REAL,
            min_ms     REAL,
            max_ms     REAL,
            jitter_ms  REAL,
            PRIMARY KEY (device_id, hour)
        );

        CREATE TABLE IF NOT EXISTS metrics (
            node_id   TEXT NOT NULL,
            ts        TEXT NOT NULL,
            key       TEXT NOT NULL,
            value_num REAL,
            value_txt TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_metrics_key_ts ON metrics(key, ts);

        -- Show Timeline: el log correlado de todos los modulos.
        CREATE TABLE IF NOT EXISTS events (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            node_id      TEXT NOT NULL,
            ts           TEXT NOT NULL,
            module       TEXT NOT NULL,
            sector       TEXT NOT NULL,
            severity     INTEGER NOT NULL,
            kind         TEXT NOT NULL,
            message      TEXT NOT NULL,
            payload_json TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_events_ts ON events(ts DESC);
        CREATE INDEX IF NOT EXISTS ix_events_module_ts ON events(module, ts DESC);

        CREATE TABLE IF NOT EXISTS alerts (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            node_id      TEXT NOT NULL,
            raised_at    TEXT NOT NULL,
            cleared_at   TEXT,
            module       TEXT NOT NULL,
            sector       TEXT NOT NULL,
            severity     INTEGER NOT NULL,
            kind         TEXT NOT NULL,
            message      TEXT NOT NULL,
            acknowledged INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_alerts_active ON alerts(cleared_at) WHERE cleared_at IS NULL;

        -- Biblioteca de EDID de referencia (M18). La clave es el conector fisico y no el
        -- monitor: la pregunta que se contesta es «por esta salida sigue estando lo mismo
        -- que el dia que funcionaba», y guardarla contra el monitor no la contestaria.
        CREATE TABLE IF NOT EXISTS edid_references (
            connector_key TEXT PRIMARY KEY,
            name          TEXT NOT NULL,
            fingerprint   TEXT NOT NULL,
            bytes         BLOB NOT NULL,
            device_name   TEXT,
            saved_at      TEXT NOT NULL
        );

        -- Marcas del propio agente sobre su base: migraciones hechas, versión del esquema.
        --
        -- Entra con la migración de los dispositivos críticos, que necesita saber si ya se
        -- hizo. La alternativa era «sembrar solo si la tabla está vacía», y eso significa
        -- que quien borre todos sus equipos y reinicie se los encuentra de vuelta.
        --
        -- Esta base no ha tenido nunca versión de esquema. Cuando haga falta una migración
        -- de verdad —renombrar una columna, partir una tabla— este es el sitio donde
        -- apuntarla, y por eso la tabla es genérica y no un `criticals_seeded INTEGER`.
        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;
}
