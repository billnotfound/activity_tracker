using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

/// <summary>
/// Applies "__idle" tag rules (see TagService.IdleTag) as SQL WHERE clauses.
/// A rule matches by process name, by window/media title pattern, or both.
/// Idle records are excluded from activity totals and from gap-derived
/// timelines (the frontend renders idle gaps itself); the summary reports
/// idle time separately as totalIdleSeconds. Semantics mirror
/// TagService.MatchesIdle.
///
/// Only strong rules (weight >= 10) apply here: weak rules (weight < 10)
/// are overridden by foreground activity (e.g. actively playing music) and
/// are applied only to media records in MediaEndpoints.
/// </summary>
public static class IdleFilter
{
    public static IQueryable<FocusChange> ExcludeIdle(
        IQueryable<FocusChange> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules.Where(r => r.Weight >= 10))
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var pat = HiddenFilter.ToLikePattern(r.TitlePattern!);
                query = query.Where(f => f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, pat, "\\"));
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var pat = HiddenFilter.ToLikePattern(r.TitlePattern!);
                query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                    || f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, pat, "\\"));
            }
        }
        return query;
    }

    public static IQueryable<WindowSession> ExcludeIdle(
        IQueryable<WindowSession> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules.Where(r => r.Weight >= 10))
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var pat = HiddenFilter.ToLikePattern(r.TitlePattern!);
                query = query.Where(w => w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, pat, "\\"));
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var pat = HiddenFilter.ToLikePattern(r.TitlePattern!);
                query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                    || w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, pat, "\\"));
            }
        }
        return query;
    }

    // ProcessSessions have no title column; only process-name rules apply.
    public static IQueryable<ProcessSession> ExcludeIdle(
        IQueryable<ProcessSession> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules.Where(r => r.Weight >= 10))
        {
            if (string.IsNullOrEmpty(rule.Process)) continue;
            var p = rule.Process;
            query = query.Where(s => EF.Functions.Collate(s.ProcessName, "NOCASE") != p);
        }
        return query;
    }
}
