using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

/// <summary>
/// Applies "__hidden" tag rules (see TagService.HiddenTag) as SQL WHERE clauses.
/// A rule can hide by process name, by window/media title pattern, or both.
/// Hidden records are excluded globally — every endpoint and the status window —
/// and never count toward totals. Semantics mirror TagService.MatchesHidden.
/// </summary>
public static class HiddenFilter
{
    // Tag rules use '*' wildcards; translate to SQLite LIKE, escaping the
    // LIKE metacharacters (% _ \) so user patterns stay literal.
    private static string ToLikePattern(string pattern) =>
        Regex.Replace(pattern, @"[\\%_]", "\\$&").Replace("*", "%");

    public static IQueryable<FocusChange> ExcludeHidden(
        IQueryable<FocusChange> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules)
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(f => f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, pat, "\\"));
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                // NOCASE collation: case-insensitive compare (StringComparison
                // overloads are not translatable)
                query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                    || f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, pat, "\\"));
            }
        }
        return query;
    }

    public static IQueryable<WindowSession> ExcludeHidden(
        IQueryable<WindowSession> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules)
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(w => w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, pat, "\\"));
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                    || w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, pat, "\\"));
            }
        }
        return query;
    }

    public static IQueryable<MediaSessionRecord> ExcludeHidden(
        IQueryable<MediaSessionRecord> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules)
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(m => m.Title == "" || !EF.Functions.Like(m.Title, pat, "\\"));
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(m => EF.Functions.Collate(m.AppName, "NOCASE") != r.Process);
            }
            else
            {
                var pat = ToLikePattern(r.TitlePattern!);
                query = query.Where(m => EF.Functions.Collate(m.AppName, "NOCASE") != r.Process
                    || m.Title == "" || !EF.Functions.Like(m.Title, pat, "\\"));
            }
        }
        return query;
    }

    // ProcessSessions have no title column; only process-name rules apply.
    public static IQueryable<ProcessSession> ExcludeHidden(
        IQueryable<ProcessSession> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Process)) continue;
            var p = rule.Process;
            query = query.Where(s => EF.Functions.Collate(s.ProcessName, "NOCASE") != p);
        }
        return query;
    }
}
