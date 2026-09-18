using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class HiddenFilter
{
    internal static string ToLikePattern(string pattern) =>
        Regex.Replace(pattern, @"[\\%_]", "\\$&").Replace("*", "%");

    internal static bool IsRegexPattern(string pattern) =>
        pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase);

    internal static bool TryGetRegexPattern(string pattern, out string regex)
    {
        regex = pattern[6..];
        try
        {
            _ = new Regex(regex, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static IQueryable<FocusChange> ExcludeHidden(
        IQueryable<FocusChange> query, IReadOnlyList<TagService.TagRule> rules)
    {
        foreach (var rule in rules)
        {
            var r = rule;
            if (string.IsNullOrEmpty(r.Process))
            {
                var titlePattern = r.TitlePattern!;
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(f => f.WindowTitle == "" || !Regex.IsMatch(f.WindowTitle, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
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
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                        || f.WindowTitle == "" || !Regex.IsMatch(f.WindowTitle, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
                    query = query.Where(f => EF.Functions.Collate(f.ProcessName, "NOCASE") != r.Process
                        || f.WindowTitle == "" || !EF.Functions.Like(f.WindowTitle, like, "\\"));
                }
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
                var titlePattern = r.TitlePattern!;
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(w => w.WindowTitle == "" || !Regex.IsMatch(w.WindowTitle, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
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
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                        || w.WindowTitle == "" || !Regex.IsMatch(w.WindowTitle, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
                    query = query.Where(w => EF.Functions.Collate(w.ProcessName, "NOCASE") != r.Process
                        || w.WindowTitle == "" || !EF.Functions.Like(w.WindowTitle, like, "\\"));
                }
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
                var titlePattern = r.TitlePattern!;
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(m => m.Title == "" || !Regex.IsMatch(m.Title, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
                    query = query.Where(m => m.Title == "" || !EF.Functions.Like(m.Title, like, "\\"));
                }
            }
            else if (string.IsNullOrEmpty(r.TitlePattern))
            {
                query = query.Where(m => EF.Functions.Collate(m.AppName, "NOCASE") != r.Process);
            }
            else
            {
                var titlePattern = r.TitlePattern;
                if (IsRegexPattern(titlePattern))
                {
                    if (!TryGetRegexPattern(titlePattern, out var regex)) continue;
                    query = query.Where(m => EF.Functions.Collate(m.AppName, "NOCASE") != r.Process
                        || m.Title == "" || !Regex.IsMatch(m.Title, regex));
                }
                else
                {
                    var like = ToLikePattern(titlePattern);
                    query = query.Where(m => EF.Functions.Collate(m.AppName, "NOCASE") != r.Process
                        || m.Title == "" || !EF.Functions.Like(m.Title, like, "\\"));
                }
            }
        }
        return query;
    }

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
