// Pre-aggregated daily stats — one row per (date, process) pair.
// NOT currently auto-populated by the tracker; reserved for future scheduled summarization.
// The unique index on (Date, ProcessName) enables upsert patterns for daily rollups.
namespace WinActivityTracker.Core.Models;

public class DailySummary
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double TotalFocusSeconds { get; set; }
    public string Category { get; set; } = string.Empty;  // Reserved for manual categorization
}
