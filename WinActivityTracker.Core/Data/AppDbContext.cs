// EF Core context backed by SQLite. Database file path is configured in Service/Program.cs
// via AppPaths (default %LOCALAPPDATA%\WinActivityTracker\activity.db, overridable).
// EnsureCreatedAsync() in Program.cs auto-creates tables on first run.
// Indexes: Timestamp (all tables, for date-range queries), ProcessName (FocusChanges, for grouping),
// and a unique composite on (Date, ProcessName) for DailySummaries upserts.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<FocusChange> FocusChanges => Set<FocusChange>();
    public DbSet<WindowSnapshot> WindowSnapshots => Set<WindowSnapshot>();  // deprecated, kept for old data
    public DbSet<WindowSession> WindowSessions => Set<WindowSession>();     // replaces WindowSnapshots
    public DbSet<ProcessSnapshot> ProcessSnapshots => Set<ProcessSnapshot>();  // deprecated
    public DbSet<ProcessSession> ProcessSessions => Set<ProcessSession>();     // replaces ProcessSnapshots
    public DbSet<MediaSessionRecord> MediaSessionRecords => Set<MediaSessionRecord>();
    public DbSet<Heartbeat> Heartbeats => Set<Heartbeat>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<ProcessIcon> ProcessIcons => Set<ProcessIcon>();
    public DbSet<ProcessIconMapping> ProcessIconMappings => Set<ProcessIconMapping>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Heartbeat>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<FocusChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);     // WHERE Timestamp >= ... queries
            e.HasIndex(x => x.ProcessName);    // GROUP BY ProcessName queries
        });

        modelBuilder.Entity<WindowSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);      // "latest snapshot" subquery
        });

        modelBuilder.Entity<ProcessSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartTime);
            e.HasIndex(x => x.EndTime);       // WHERE EndTime IS NULL (active process session)
        });

        modelBuilder.Entity<ProcessSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);      // "latest snapshot" subquery
        });

        modelBuilder.Entity<MediaSessionRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartTime);      // ORDER BY StartTime DESC
            e.HasIndex(x => x.EndTime);        // WHERE EndTime IS NULL (active session)
        });

        modelBuilder.Entity<WindowSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OpenTime);       // query open windows: WHERE CloseTime IS NULL
        });

        modelBuilder.Entity<SystemEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<DailySummary>(e =>
        {
            e.HasKey(x => x.Id);
            // Ensures one summary row per process per day — enables INSERT OR REPLACE patterns.
            e.HasIndex(x => new { x.Date, x.ProcessName }).IsUnique();
        });

        modelBuilder.Entity<ProcessIcon>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IconHash).IsUnique();
        });

        modelBuilder.Entity<ProcessIconMapping>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProcessName);
            e.HasIndex(x => x.IconHash);
        });
    }
}
