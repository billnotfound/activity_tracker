using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

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
                var titlePattern = r.TitlePattern!;
                if (HiddenFilter.IsRegexPattern(titlePattern))
                {
                    if (!HiddenFilter.TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(f => f.WindowTitle == "" || !Regex.IsMatch(f.WindowTitle, regex));
                }
                else
                {
                    var like = HiddenFilter.ToLikePattern(titlePattern);
                    query = query.Where(f => f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, like, "\\"));
                }
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var titlePattern = r.TitlePattern;
                if (HiddenFilter.IsRegexPattern(titlePattern))
                {
                    if (!HiddenFilter.TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                        || f.WindowTitle == "" || !Regex.IsMatch(f.WindowTitle, regex));
                }
                else
                {
                    var like = HiddenFilter.ToLikePattern(titlePattern);
                    query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                        || f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, like, "\\"));
                }
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
                var titlePattern = r.TitlePattern!;
                if (HiddenFilter.IsRegexPattern(titlePattern))
                {
                    if (!HiddenFilter.TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(w => w.WindowTitle == "" || !Regex.IsMatch(w.WindowTitle, regex));
                }
                else
                {
                    var like = HiddenFilter.ToLikePattern(titlePattern);
                    query = query.Where(w => w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, like, "\\"));
                }
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process);
            }
            else
            {
                var titlePattern = r.TitlePattern;
                if (HiddenFilter.IsRegexPattern(titlePattern))
                {
                    if (!HiddenFilter.TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                        || w.WindowTitle == "" || !Regex.IsMatch(w.WindowTitle, regex));
                }
                else
                {
                    var like = HiddenFilter.ToLikePattern(titlePattern);
                    query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                        || w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, like, "\\"));
                }
            }
        }
        return query;
    }

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
