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
            // Eine langlebige Verbindung — ohne Pool wird die Datei bei Dispose sofort freigegeben.
            Pooling = false,
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
        Execute("""
            CREATE TABLE IF NOT EXISTS uph_samples (
                Bucket        TEXT    NOT NULL,
                ResourcePoint TEXT    NOT NULL,
                Destination   TEXT    NOT NULL,
                Orders        INTEGER NOT NULL,
                PRIMARY KEY (Bucket, ResourcePoint, Destination)
            );
            """);
        Execute("CREATE INDEX IF NOT EXISTS ix_uph_samples_bucket ON uph_samples(Bucket);");
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
            dateTime.Value = Stamp(t.DateTime);
            direction.Value = (int)t.TelegramDirection;
            connection.Value = (object?)t.ConnectionName ?? DBNull.Value;
            data.Value = (object?)t.Data ?? DBNull.Value;
            format.Value = (object?)t.Format ?? DBNull.Value;
            written += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return written;
    }

    /// <summary>
    /// Stichtag für die Aufbewahrung: Telegramme mit früherem <c>DateTime</c> dürfen gelöscht werden.
    /// Ohne Zeitzonen-Anteil formatiert, damit der String-Vergleich zu den gespeicherten Werten passt.
    /// </summary>
    public static DateTime RetentionCutoff(int retentionDays) =>
        DateTime.SpecifyKind(DateTime.Now.AddDays(-retentionDays), DateTimeKind.Unspecified);

    /// <summary>Löscht Telegramme, deren <c>DateTime</c> vor dem Stichtag liegt. Gibt die Anzahl zurück.</summary>
    public int DeleteOlderThan(DateTime cutoffExclusive)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM telegrams WHERE DateTime < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", Stamp(cutoffExclusive));
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Einheitliche, zeitzonenfreie String-Darstellung für Spalte <c>DateTime</c> — so bleiben
    /// gespeicherte Werte und Abfragegrenzen als String vergleichbar, egal welchen <c>Kind</c>
    /// die Anlage liefert.
    /// </summary>
    static string Stamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Gibt nach großen Löschungen belegten Speicher an das Betriebssystem zurück.</summary>
    public void Vacuum() => Execute("VACUUM;");

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

    /// <summary>
    /// <c>DateTime</c> des jüngsten Telegramms — der rechte Rand des Auswertungsfensters.
    /// Anker in der gespeicherten Zeit-Basis, unabhängig davon, ob die Anlage lokale Zeit oder
    /// UTC schickt. <c>null</c>, solange die Tabelle leer ist.
    /// </summary>
    public DateTime? MaxTelegramTime()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT MAX(DateTime) FROM telegrams;";
        return command.ExecuteScalar() is string s && s.Length > 0
            ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    /// <summary><c>DateTime</c> des ältesten Telegramms, oder <c>null</c> bei leerer Tabelle.</summary>
    public DateTime? MinTelegramTime()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT MIN(DateTime) FROM telegrams;";
        return command.ExecuteScalar() is string s && s.Length > 0
            ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    /// <summary>
    /// Sekunden seit dem letzten Schreibvorgang (Spalte <c>RecordedAt</c>, unsere UTC-Uhr) —
    /// zeigt, ob der Recorder noch Telegramme ablegt. <c>null</c>, solange nichts geschrieben wurde.
    /// </summary>
    public double? SecondsSinceLastWrite()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT MAX(RecordedAt) FROM telegrams;";
        if (command.ExecuteScalar() is not string s || s.Length == 0)
            return null;

        var last = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
        return Math.Round((DateTime.UtcNow - last).TotalSeconds, 1);
    }

    /// <summary>Liest aufgezeichnete Telegramme, optional auf einen Zeitraum eingegrenzt.</summary>
    public IEnumerable<Telegram> Read(DateTime? from, DateTime? to)
    {
        using var command = _connection.CreateCommand();
        var where = new List<string>();
        if (from.HasValue)
        {
            where.Add("DateTime >= $from");
            command.Parameters.AddWithValue("$from", Stamp(from.Value));
        }
        if (to.HasValue)
        {
            where.Add("DateTime <= $to");
            command.Parameters.AddWithValue("$to", Stamp(to.Value));
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

    // ---- UPH-Historie (verdichtete Zähler, eigene Aufbewahrung) --------------------------------

    /// <summary>Jüngstes bereits verdichtetes Zeitraster, oder <c>null</c> solange nichts verdichtet wurde.</summary>
    public DateTime? MaxUphBucket()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT MAX(Bucket) FROM uph_samples;";
        return command.ExecuteScalar() is string s && s.Length > 0
            ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    public long UphSampleCount()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM uph_samples;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Ersetzt alle Rasterzeilen ab <paramref name="from"/> (einschließlich) durch
    /// <paramref name="rows"/> — in einer Transaktion, damit die Verdichtung wiederholbar ist
    /// (das jüngste, evtl. noch unvollständige Raster wird beim nächsten Lauf neu gerechnet).
    /// </summary>
    public void ReplaceUphSamplesFrom(DateTime from, IEnumerable<UphSampleRow> rows)
    {
        using var transaction = _connection.BeginTransaction();

        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM uph_samples WHERE Bucket >= $from;";
            delete.Parameters.AddWithValue("$from", Stamp(from));
            delete.ExecuteNonQuery();
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO uph_samples (Bucket, ResourcePoint, Destination, Orders)
                VALUES ($bucket, $resourcePoint, $destination, $orders)
                ON CONFLICT(Bucket, ResourcePoint, Destination) DO UPDATE SET Orders = excluded.Orders;
                """;
            var bucket = insert.Parameters.Add("$bucket", SqliteType.Text);
            var resourcePoint = insert.Parameters.Add("$resourcePoint", SqliteType.Text);
            var destination = insert.Parameters.Add("$destination", SqliteType.Text);
            var orders = insert.Parameters.Add("$orders", SqliteType.Integer);

            foreach (var row in rows)
            {
                bucket.Value = Stamp(row.Bucket);
                resourcePoint.Value = row.ResourcePoint;
                destination.Value = row.Destination;
                orders.Value = row.Orders;
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>Löscht Rasterzeilen vor dem Stichtag. Gibt die Anzahl zurück.</summary>
    public int DeleteUphSamplesOlderThan(DateTime cutoffExclusive)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM uph_samples WHERE Bucket < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", Stamp(cutoffExclusive));
        return command.ExecuteNonQuery();
    }

    /// <summary>Rasterzeilen im Halbbereich <c>[from, to)</c>, aufsteigend.</summary>
    public IReadOnlyList<UphSampleRow> ReadUphSamples(DateTime from, DateTime to)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT Bucket, ResourcePoint, Destination, Orders FROM uph_samples
            WHERE Bucket >= $from AND Bucket < $to
            ORDER BY Bucket;
            """;
        command.Parameters.AddWithValue("$from", Stamp(from));
        command.Parameters.AddWithValue("$to", Stamp(to));

        var list = new List<UphSampleRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UphSampleRow
            {
                Bucket = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                ResourcePoint = reader.GetString(1),
                Destination = reader.GetString(2),
                Orders = reader.GetInt32(3),
            });
        }
        return list;
    }

    void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
