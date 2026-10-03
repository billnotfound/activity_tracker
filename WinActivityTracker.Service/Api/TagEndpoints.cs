using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class TagEndpoints
{
    private static readonly SemaphoreSlim _saveGate = new(1, 1);
    private static readonly JsonSerializerOptions _saveOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void MapTagEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tags/status", GetTagStatus);
        app.MapPut("/api/tags/save", SaveTagRules);
    }

    private static IResult GetTagStatus(TagService tagService,
        [FromServices] TitleNormalizer titleNormalizer,
        [FromServices] AppPaths appPaths)
    {
        var tagsPath = Path.Combine(appPaths.ConfigDir, "tags.json");
        var titleRulesPath = Path.Combine(appPaths.ConfigDir, "title_rules.json");

        return Results.Ok(new
        {
            tags = new
            {
                rules = tagService.GetRules(),
                error = tagService.ConfigError,
                lastWrite = ConfigWritePrecondition.GetLastWrite(tagsPath)
            },
            titleRules = new
            {
                rules = titleNormalizer.GetRules(),
                error = titleNormalizer.ConfigError,
                lastWrite = ConfigWritePrecondition.GetLastWrite(titleRulesPath)
            }
        });
    }

    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static async Task<IResult> SaveTagRules(HttpRequest request, AppPaths appPaths)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var rules = JsonSerializer.Deserialize<List<TagService.TagRule>>(body, _readOptions);

            if (rules == null || rules.Count == 0)
                return Results.BadRequest(new { error = "Rule list is empty." });

            // "__hidden" and "__idle" are special internal tags and must survive the save.
            var skipped = rules
                .Where(r => r.Tag.StartsWith('_') && r.Tag != TagService.HiddenTag && r.Tag != TagService.IdleTag)
                .ToList();
            rules = rules
                .Where(r => !r.Tag.StartsWith('_') || r.Tag == TagService.HiddenTag || r.Tag == TagService.IdleTag)
                .ToList();

            if (rules.Count == 0)
                return Results.BadRequest(new { error = I18nService._("tags.allSkippedPrefix") });

            var path = Path.Combine(appPaths.ConfigDir, "tags.json");
            var json = JsonSerializer.Serialize(rules, _saveOptions);
            await _saveGate.WaitAsync(request.HttpContext.RequestAborted);
            try
            {
                if (ConfigWritePrecondition.Validate(request, path) is { } conflict)
                    return conflict;
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, json, request.HttpContext.RequestAborted);
                File.Move(tmp, path, overwrite: true);
            }
            finally { _saveGate.Release(); }

            if (skipped.Count > 0)
            {
                return Results.Ok(new
                {
                    saved = rules.Count,
                    skipped = skipped.Count,
                    skippedTags = skipped.Select(s => s.Tag).ToList(),
                    message = I18nService._("tags.rulesSaved") + " (" + I18nService._("tags.skippedPrefixCount", skipped.Count) + ")"
                });
            }

            return Results.Ok(new { saved = rules.Count, message = I18nService._("tags.rulesSaved") });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = I18nService._("tags.jsonError", ex.Message) });
        }
        catch (Exception ex)
        {
            return Results.Problem(I18nService._("error.saveFailed", ex.Message));
        }
    }
}
