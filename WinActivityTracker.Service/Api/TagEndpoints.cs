using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class TagEndpoints
{
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

    private static IResult GetTagStatus(TagService tagService, [FromServices] TitleNormalizer titleNormalizer)
    {
        var tagsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinActivityTracker", "tags.json");
        var titleRulesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinActivityTracker", "title_rules.json");

        return Results.Ok(new
        {
            tags = new
            {
                rules = tagService.GetRules(),
                error = tagService.ConfigError,
                lastWrite = File.Exists(tagsPath)
                    ? File.GetLastWriteTimeUtc(tagsPath).ToString("o") : null
            },
            titleRules = new
            {
                rules = titleNormalizer.GetRules(),
                error = titleNormalizer.ConfigError,
                lastWrite = File.Exists(titleRulesPath)
                    ? File.GetLastWriteTimeUtc(titleRulesPath).ToString("o") : null
            }
        });
    }

    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static async Task SaveTagRules(HttpRequest request, HttpResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var rules = JsonSerializer.Deserialize<List<TagService.TagRule>>(body, _readOptions);

            if (rules == null || rules.Count == 0)
            {
                response.StatusCode = 400;
                await response.WriteAsJsonAsync(new { error = "Rule list is empty." });
                return;
            }

            rules = rules.Where(r => !r.Tag.StartsWith('_')).ToList();

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinActivityTracker");
            var path = Path.Combine(dir, "tags.json");

            var json = JsonSerializer.Serialize(rules, _saveOptions);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, path, overwrite: true);

            response.StatusCode = 200;
            await response.WriteAsJsonAsync(new { saved = rules.Count, message = "tags.json 已保存，热重载即刻生效。" });
        }
        catch (JsonException ex)
        {
            response.StatusCode = 400;
            await response.WriteAsJsonAsync(new { error = $"JSON 解析或序列化失败: {ex.Message}" });
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await response.WriteAsJsonAsync(new { error = $"保存失败: {ex.Message}" });
        }
    }
}
