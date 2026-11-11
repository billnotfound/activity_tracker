using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;

namespace WinActivityTracker.Service.Api;

internal static class LegacyDatabaseImport
{
    private const long MaxBytes = 1024L * 1024 * 1024;
    private static readonly SemaphoreSlim ImportGate = new(1);
    private static readonly string[] TimeFormats =
        ["yyyy-MM-dd HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    // Keep existing rows when the recorded identity matches. A window or media
    // session may have had its end time corrected since the old copy was made.
    private static readonly TableSpec[] Tables =
    [
        new("FocusChanges", ["Timestamp", "ProcessName", "WindowTitle", "DurationSeconds"],
            ["Timestamp"], ["Timestamp", "ProcessName", "DurationSeconds"],
            "Timestamp", "ProcessName", "typeof(DurationSeconds) IN ('integer','real') AND DurationSeconds BETWEEN 0 AND 315537897599"),
        new("WindowSessions", ["OpenTime", "ProcessName", "WindowTitle", "CloseTime"],
            ["OpenTime", "CloseTime"], ["OpenTime", "ProcessName", "WindowTitle"],
            "OpenTime", "ProcessName", "CloseTime >= OpenTime"),
        new("ProcessSessions", ["StartTime", "ProcessName", "ProcessId", "EndTime"],
            ["StartTime", "EndTime"], ["StartTime", "ProcessName", "ProcessId"],
            "StartTime", "ProcessName", "typeof(ProcessId) = 'integer' AND ProcessId BETWEEN 0 AND 2147483647 AND EndTime >= StartTime"),
        new("MediaSessionRecords", ["StartTime", "AppName", "Title", "Artist", "PlaybackStatus", "EndTime"],
            ["StartTime", "EndTime"], ["StartTime", "AppName", "Title", "Artist", "PlaybackStatus"],
            "StartTime", "Title", "EndTime >= StartTime"),
        new("SystemEvents", ["Timestamp", "EventType", "DurationSeconds"],
            ["Timestamp"], ["Timestamp", "EventType"], "Timestamp", "EventType",
            "EventType IN ('Sleep','Wake','Shutdown','Start','Idle') AND typeof(DurationSeconds) IN ('integer','real') AND DurationSeconds BETWEEN 0 AND 315537897599"),
        new("WindowSnapshots", ["Timestamp", "ProcessName", "WindowTitle", "IsFocused"],
            ["Timestamp"], ["Timestamp", "ProcessName", "WindowTitle", "IsFocused"],
            "Timestamp", "ProcessName", "typeof(IsFocused) = 'integer' AND IsFocused IN (0,1)"),
        new("ProcessSnapshots", ["Timestamp", "ProcessName", "ProcessId"],
            ["Timestamp"], ["Timestamp", "ProcessName", "ProcessId"], "Timestamp", "ProcessName",
            "typeof(ProcessId) = 'integer' AND ProcessId BETWEEN 0 AND 2147483647"),
    ];

    public static Task<IResult> PreviewAsync(HttpContext context, AppDbContext db, ILoggerFactory logs)
        => HandleAsync(context, db, logs, expectedHash: null);

    public static Task<IResult> ImportAsync(HttpContext context, AppDbContext db, ILoggerFactory logs, string sha256)
        => sha256.Length == 64 && sha256.All(Uri.IsHexDigit)
            ? HandleAsync(context, db, logs, sha256)
            : Task.FromResult(Results.BadRequest(new { code = "fileChanged" }));

    private static async Task<IResult> HandleAsync(HttpContext context, AppDbContext db, ILoggerFactory logs, string? expectedHash)
    {
        var ct = context.RequestAborted;
        if (context.Request.ContentLength is > MaxBytes)
            return Results.BadRequest(new { code = "fileTooLarge" });
        if (!await ImportGate.WaitAsync(0, ct))
            return Results.Conflict(new { code = "busy" });

        var upload = Path.Combine(Path.GetTempPath(), $"wta-import-{Guid.NewGuid():N}.db");
        try
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = MaxBytes;
            var hash = await SaveUploadAsync(context.Request.Body, upload, ct);
            if (expectedHash != null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { code = "fileChanged" });

            var result = Merge(db.Database.GetConnectionString()!, upload, expectedHash != null, ct);
            return Results.Ok(new { sha256 = hash, tables = result });
        }
        catch (ImportException ex) { return Results.BadRequest(new { code = ex.Message }); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { return Results.BadRequest(new { code = "fileTooLarge" }); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        { return Results.BadRequest(new { code = "invalidDatabase" }); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        { return Results.Conflict(new { code = "busy" }); }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            logs.CreateLogger("LegacyDatabaseImport").LogWarning(ex, "Legacy database import failed");
            return Results.Json(new { code = "failed" }, statusCode: 500);
        }
        finally
        {
            try { File.Delete(upload); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logs.CreateLogger("LegacyDatabaseImport").LogWarning(ex, "Could not remove staged database"); }
            finally { ImportGate.Release(); }
        }
    }

    private static async Task<string> SaveUploadAsync(Stream input, string destination, CancellationToken ct)
    {
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = new byte[16];
        if (await input.ReadAtLeastAsync(header, header.Length, false, ct) != header.Length
            || !header.AsSpan().SequenceEqual("SQLite format 3\0"u8))
            throw new ImportException("invalidDatabase");
        await file.WriteAsync(header, ct);
        hash.AppendData(header);
        var buffer = new byte[128 * 1024];
        long size = header.Length;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            size += read;
            if (size > MaxBytes) throw new ImportException("fileTooLarge");
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static IReadOnlyList<TablePreview> Merge(string targetConnectionString, string sourceFile, bool import, CancellationToken ct)
    {
        var settings = new SqliteConnectionStringBuilder(targetConnectionString)
        {
            Mode = import ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        // The first open must use URI syntax for SQLite to honor URI options
        // on the attached backup too.
        if (!settings.DataSource.StartsWith("file:", StringComparison.Ordinal))
            settings.DataSource = new Uri(Path.GetFullPath(settings.DataSource)).AbsoluteUri;
        using var db = new SqliteConnection(settings.ToString());
        db.Open();
        db.CreateFunction<string?, string?>("import_utc", NormalizeTimestamp, isDeterministic: true);
        Execute(db, "PRAGMA trusted_schema=OFF");
        using (var attach = Command(db, "ATTACH DATABASE $source AS legacy"))
        {
            attach.Parameters.AddWithValue("$source", new Uri(Path.GetFullPath(sourceFile)).AbsoluteUri + "?mode=ro&immutable=1");
            attach.ExecuteNonQuery();
        }
        if (!Equals(Scalar(db, "PRAGMA legacy.quick_check(1)"), "ok"))
            throw new ImportException("invalidDatabase");

        var sources = ReadNames(db, "SELECT name FROM pragma_table_list WHERE schema='legacy' AND type='table'");
        var staged = new List<StagedTable>();
        foreach (var table in Tables.Where(t => sources.Contains(t.Name)))
        {
            ct.ThrowIfCancellationRequested();
            staged.Add(Stage(db, table));
        }
        if (staged.Count == 0) throw new ImportException("unsupportedSchema");
        Execute(db, "DETACH DATABASE legacy");

        // Hold the writer lock while comparing and inserting, so two copies of
        // the same import cannot both insert the same rows.
        using var tx = db.BeginTransaction(deferred: !import);
        var result = new List<TablePreview>(staged.Count);
        foreach (var item in staged)
        {
            ct.ThrowIfCancellationRequested();
            var table = item.Spec;
            var keys = string.Join(" AND ", table.Keys.Select(k => $"a.{Q(k)} = s.{Q(k)}"));
            var names = string.Join(", ", table.Columns.Select(Q));
            var missing = $"FROM temp.{Q(table.Name)} s WHERE NOT EXISTS (SELECT 1 FROM main.{Q(table.Name)} a WHERE {keys})";
            var added = import
                ? Execute(db, $"INSERT INTO main.{Q(table.Name)} ({names}) SELECT {names} {missing}", tx)
                : Convert.ToInt64(Scalar(db, $"SELECT count(*) {missing}", tx), CultureInfo.InvariantCulture);
            result.Add(new TablePreview(table.Name, item.SourceRows, added,
                item.ValidRows - added, item.SourceRows - item.ValidRows, item.From, item.To, item.Samples));
        }
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return result;
    }

    private static StagedTable Stage(SqliteConnection db, TableSpec table)
    {
        var available = ReadNames(db, $"SELECT name FROM pragma_table_info('{table.Name}', 'legacy')");
        var pointMedia = table.Name == "MediaSessionRecords" && available.Contains("Timestamp") && !available.Contains("StartTime");
        string SourceColumn(string column) => pointMedia && column == "StartTime" ? "Timestamp" : column;
        string Expression(string column) => pointMedia && column == "EndTime" ? "import_utc(Timestamp)"
            : table.Times.Contains(column) ? $"import_utc({Q(SourceColumn(column))})" : Q(column);
        if (!available.Contains("Id") || table.Columns.Any(c => !(pointMedia && c == "EndTime") && !available.Contains(SourceColumn(c))))
            throw new ImportException("unsupportedSchema");

        var input = $"legacy.{Q(table.Name)}";
        var name = $"temp.{Q(table.Name)}";
        var fields = string.Join(", ", table.Columns.Select(c => $"{Expression(c)} AS {Q(c)}"));
        var names = string.Join(", ", table.Columns.Select(Q));
        var nonNull = string.Join(" AND ", table.Columns.Select(c => $"{Q(c)} IS NOT NULL"));
        var textFields = table.Columns.Except(table.Times).Where(c => c is not ("ProcessId" or "DurationSeconds" or "IsFocused"));
        var strings = string.Join(" AND ", textFields.Select(c => $"typeof({Q(c)})='text'"));
        var valid = $"{nonNull} AND {strings} AND {table.Validation}";
        var identity = string.Join(", ", table.Keys.Select(Q));
        var sourceRows = Convert.ToInt64(Scalar(db, $"SELECT count(*) FROM {input}"), CultureInfo.InvariantCulture);
        var stageName = "temp.import_stage";
        Execute(db, $"CREATE TEMP TABLE import_stage AS SELECT Id, {fields} FROM {input}");
        long validRows;
        string? from;
        string? to;
        using (var stats = Command(db, $"SELECT count(*), min({Q(table.Start)}), max({Q(table.Times.Last())}) FROM {stageName} WHERE {valid}"))
        using (var reader = stats.ExecuteReader())
        {
            reader.Read();
            validRows = reader.GetInt64(0);
            from = reader.IsDBNull(1) ? null : reader.GetString(1);
            to = reader.IsDBNull(2) ? null : reader.GetString(2);
        }
        Execute(db, $"""
            CREATE TEMP TABLE {Q(table.Name)} AS SELECT {names} FROM (
              SELECT {names}, row_number() OVER (PARTITION BY {identity} ORDER BY Id) AS ImportRow
              FROM {stageName} WHERE {valid}
            ) WHERE ImportRow=1
            """);
        Execute(db, $"DROP TABLE {stageName}");
        var samples = new List<ImportSample>();
        using var sample = Command(db, $"SELECT {Q(table.Start)}, substr({Q(table.Label)},1,120) FROM {name} ORDER BY {Q(table.Start)} DESC LIMIT 3");
        using var rows = sample.ExecuteReader();
        while (rows.Read()) samples.Add(new(rows.GetString(0), rows.GetString(1)));
        return new StagedTable(table, sourceRows, validRows, from, to, samples);
    }

    private static string? NormalizeTimestamp(string? value) => DateTime.TryParseExact(value, TimeFormats,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
        ? result.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) : null;

    private static string Q(string name) => $"\"{name}\"";
    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }
    private static int Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    { using var cmd = Command(db, sql, tx); return cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    { using var cmd = Command(db, sql, tx); return cmd.ExecuteScalar(); }
    private static HashSet<string> ReadNames(SqliteConnection db, string sql)
    {
        using var cmd = Command(db, sql);
        using var rows = cmd.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (rows.Read()) names.Add(rows.GetString(0));
        return names;
    }

    private sealed record TableSpec(string Name, string[] Columns, string[] Times, string[] Keys, string Start, string Label, string Validation);
    private sealed record StagedTable(TableSpec Spec, long SourceRows, long ValidRows, string? From, string? To, List<ImportSample> Samples);
    internal sealed record ImportSample(string Timestamp, string Label);
    internal sealed record TablePreview(string Table, long SourceRows, long Added, long Duplicate, long Invalid, string? From, string? To, List<ImportSample> Samples);
    private sealed class ImportException(string code) : Exception(code);
}
