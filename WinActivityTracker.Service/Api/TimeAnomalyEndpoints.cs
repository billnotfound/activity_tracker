// 时间异常 API（Task 9），仿 AdminEndpoints 静态扩展模式。
// 消费：TimeAnomalyService（列表/NTP 触发）、NtpSyncService（状态）、
// TimeOffsetApplyService（预览/应用/恢复）。Ignore 直接对 AppDbContext 做条件更新
// （与 AdminEndpoints 的 RunCleanup/RunReset 一致，避免为单端点扩展服务面）。
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class TimeAnomalyEndpoints
{
    public static void MapTimeAnomalyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/time-anomalies", GetList);
        app.MapPost("/api/time-anomalies/{id:long}/apply", Apply);
        app.MapPost("/api/time-anomalies/{id:long}/restore", Restore);
        app.MapPost("/api/time-anomalies/{id:long}/ignore", Ignore);
        app.MapPost("/api/time-anomalies/ntp-check", NtpCheck);
    }

    private static IResult GetList(TimeAnomalyService svc, NtpSyncService ntp, SettingsService settings, int limit = 50)
        => Results.Ok(new
        {
            items = svc.GetAnomalies(Math.Clamp(limit, 1, 500)),
            ntp = new
            {
                useNtp = settings.Settings.UseNtp,
                lastQueryAt = ntp.LastQueryAt,
                succeeded = ntp.LastResult?.Succeeded,
                offsetSeconds = ntp.LastResult?.OffsetSeconds,
                latencyMs = ntp.LastResult?.LatencyMs,
                sourceName = ntp.LastResult?.SourceName
            }
        });

    /// <summary>
    /// apply：preview=true → 只返回每表受影响行数；否则真实应用并返回结果。
    /// direction（pre/post）仅 EventLog Pending 行必填；Heartbeat 行由偏移符号决定。
    /// </summary>
    private static async Task<IResult> Apply(TimeOffsetApplyService apply, long id,
        bool preview = false, string? direction = null)
    {
        if (preview)
        {
            var p = await apply.PreviewAsync(id, direction);
            return p == null ? Results.NotFound(new { error = "anomaly not found" })
                : Results.Ok(new { tableCounts = p.TableCounts });
        }
        var r = await apply.Apply(id, direction);
        return r == null ? Results.NotFound(new { error = "anomaly not found" })
            : Results.Ok(r);
    }

    /// <summary>按 anomalyId 找最近一次未恢复的 application 并恢复。</summary>
    private static async Task<IResult> Restore(TimeOffsetApplyService apply, long id)
    {
        var r = await apply.RestoreLatestForAnomaly(id);
        return r == null ? Results.NotFound(new { error = "no application to restore" })
            : Results.Ok(r);
    }

    /// <summary>仅 Suspicious/Drift 可忽略。条件原子更新：非该状态返回 404。</summary>
    private static async Task<IResult> Ignore(AppDbContext db, long id)
    {
        var affected = await db.TimeAnomalies
            .Where(a => a.Id == id &&
                (a.Status == TimeAnomalyStatus.Suspicious || a.Status == TimeAnomalyStatus.Drift))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, TimeAnomalyStatus.Ignored));
        return affected > 0 ? Results.Ok(new { ignored = true })
            : Results.NotFound(new { error = "anomaly not found or not ignorable" });
    }

    private static async Task<IResult> NtpCheck(TimeAnomalyService svc)
    {
        var r = await svc.CheckNtpNow();
        return Results.Ok(r);
    }
}
