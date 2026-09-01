using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kcc.Recorder;

/// <summary>Lokale SQLite-Ablage der mitgeschnittenen Telegramme.</summary>
public sealed class TelegramStore : IDisposable
{
    const string LastSeenIdKey = "LastSeenId";

    readonly SqliteConnection _connection;

    public TelegramStore(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _connection.Open();
        Initialize();
    }

    void Initialize()
    {
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS telegrams (
                Id                INTEGER PRIMARY KEY,
                DateTime          TEXT    NOT NULL,
                TelegramDirection INTEGER NOT NULL,
                ConnectionName    TEXT,
                Data              TEXT,
                Format            TEXT,
                RecordedAt        TEXT    NOT NULL
            );
            """);
        Execute("CREATE INDEX IF NOT EXISTS ix_telegrams_datetime ON telegrams(DateTime);");
        Execute("CREATE INDEX IF NOT EXISTS ix_telegrams_connection ON telegrams(ConnectionName, Id);");
        Execute("""
            CREATE TABLE IF NOT EXISTS recorder_state (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);
    }

    /// <summary>Schreibt einen Stapel in einer Transaktion. Bereits vorhandene Ids werden übersprungen.</summary>
    public int Insert(IEnumerable<Telegram> telegrams)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO telegrams
                (Id, DateTime, TelegramDirection, ConnectionName, Data, Format, RecordedAt)
            VALUES ($id, $dateTime, $direction, $connection, $data, $format, $recordedAt);
            """;

        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var dateTime = command.Parameters.Add("$dateTime", SqliteType.Text);
        var direction = command.Parameters.Add("$direction", SqliteType.Integer);
        var connection = command.Parameters.Add("$connection", SqliteType.Text);
        var data = command.Parameters.Add("$data", SqliteType.Text);
        var format = command.Parameters.Add("$format", SqliteType.Text);
        var recordedAt = command.Parameters.Add("$recordedAt", SqliteType.Text);
        recordedAt.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var written = 0;
        foreach (var t in telegrams)
        {
            id.Value = t.Id;
            dateTime.Value = t.DateTime.ToString("O", CultureInfo.InvariantCulture);
            direction.Value = (int)t.TelegramDirection;
            connection.Value = (object?)t.ConnectionName ?? DBNull.Value;
            data.Value = (object?)t.Data ?? DBNull.Value;
            format.Value = (object?)t.Format ?? DBNull.Value;
            written += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return written;
    }

    /// <summary>Höchste bereits verarbeitete Telegramm-Id — auch für Zeilen, die der Filter verworfen hat.</summary>
    public long? GetLastSeenId()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Value FROM recorder_state WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", LastSeenIdKey);
        var value = command.ExecuteScalar() as string;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    public void SetLastSeenId(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recorder_state (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", LastSeenIdKey);
        command.Parameters.AddWithValue("$value", id.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public long Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM telegrams;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Liest aufgezeichnete Telegramme, optional auf einen Zeitraum eingegrenzt.</summary>
    public IEnumerable<Telegram> Read(DateTime? from, DateTime? to)
    {
        using var command = _connection.CreateCommand();
        var where = new List<string>();
        if (from.HasValue)
        {
            where.Add("DateTime >= $from");
            command.Parameters.AddWithValue("$from", from.Value.ToString("O", CultureInfo.InvariantCulture));
        }
        if (to.HasValue)
        {
            where.Add("DateTime <= $to");
            command.Parameters.AddWithValue("$to", to.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        command.CommandText =
            "SELECT Id, DateTime, TelegramDirection, ConnectionName, Data, Format FROM telegrams" +
            (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
            " ORDER BY Id;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new Telegram(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                (TelegramDirection)reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5));
        }
    }

    void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
