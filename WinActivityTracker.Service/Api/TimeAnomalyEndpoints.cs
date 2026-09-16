// 时间异常 API，沿用 AdminEndpoints 的静态扩展模式。
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
        app.MapPost("/api/time-anomalies/manual", ApplyManual);
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
    /// direction（pre/post）可显式指定；未指定时重新查询时间参照，不能判定则返回 404。
    /// </summary>
    private static async Task<IResult> Apply(TimeOffsetApplyService apply, NtpSyncService ntp, long id,
        bool preview = false, string? direction = null)
    {
        var reference = string.IsNullOrWhiteSpace(direction)
            ? await ntp.RequestCheckAsync()
            : null;
        if (preview)
        {
            var p = await apply.PreviewAsync(id, direction, reference);
            return p == null ? Results.NotFound(new { error = "anomaly not found" })
                : Results.Ok(new { tableCounts = p.TableCounts });
        }
        var r = await apply.Apply(id, direction, reference);
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

    private static async Task<IResult> ApplyManual(
        TimeOffsetApplyService apply, ManualTimeCorrection request, bool preview = false)
    {
        var from = NormalizeToUtc(request.From);
        var to = NormalizeToUtc(request.To);
        if (to <= from || request.ShiftSeconds == 0 || !double.IsFinite(request.ShiftSeconds)
            || Math.Abs(request.ShiftSeconds) > TimeSpan.FromDays(365).TotalSeconds)
            return Results.BadRequest(new { error = "invalid manual time correction" });

        if (preview)
        {
            var result = await apply.PreviewManualRangeAsync(from, to, request.ShiftSeconds);
            return result == null
                ? Results.BadRequest(new { error = "invalid manual time correction" })
                : Results.Ok(new { tableCounts = result.TableCounts });
        }

        var applied = await apply.ApplyManualRangeAsync(from, to, request.ShiftSeconds, request.Note);
        return applied == null
            ? Results.NotFound(new { error = "no rows in selected range" })
            : Results.Ok(applied);
    }

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };
}

public sealed record ManualTimeCorrection(
    DateTime From, DateTime To, double ShiftSeconds, string? Note);
