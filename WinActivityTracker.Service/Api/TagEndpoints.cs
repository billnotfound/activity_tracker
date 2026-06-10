using Microsoft.AspNetCore.Mvc;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tags/status", GetTagStatus);
    }

    private static IResult GetTagStatus(TagService tagService, [FromServices] TitleNormalizer titleNormalizer)
    {
        return Results.Ok(new
        {
            tags = new
            {
                rules = tagService.GetRules(),
                error = tagService.ConfigError
            },
            titleRules = new
            {
                rules = titleNormalizer.GetRules(),
                error = titleNormalizer.ConfigError
            }
        });
    }
}
