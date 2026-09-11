using System.Data;
using Microsoft.EntityFrameworkCore;

namespace WinActivityTracker.Core.Data;

public static class SqliteMaintenance
{
    public const long JournalSizeLimitBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Applies a physical WAL size limit and asks SQLite to checkpoint+truncate.
    /// TRUNCATE reports busy as a result row rather than throwing, so inspect it
    /// and retry briefly after transient dashboard readers release their snapshot.
    /// </summary>
    public static async Task<bool> TryTruncateWalAsync(
        AppDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone)
            await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using (var limit = connection.CreateCommand())
            {
                limit.CommandText = $"PRAGMA journal_size_limit={JournalSizeLimitBytes}";
                await limit.ExecuteScalarAsync(cancellationToken);
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                await using var reader = await checkpoint.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken) && reader.GetInt32(0) == 0)
                    return true;
                if (attempt < 9)
                    await Task.Delay(100, cancellationToken);
            }
            return false;
        }
        finally
        {
            if (closeWhenDone)
                await db.Database.CloseConnectionAsync();
        }
    }
}
