using Meducate.API.Infrastructure;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;

namespace Meducate.API.Endpoints;

internal static class TopicEndpoints
{
    internal static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/topics/types", [RequiresApiKey] async (ITopicRepository repo, CancellationToken ct) =>
        {
            var types = await repo.GetDistinctTypesAsync(ct);
            return Results.Ok(types);
        })
        .WithName("ListTopicTypes")
        .WithSummary("List available topic types")
        .WithDescription("Returns a list of distinct topic types currently in the database (e.g. Disease, Drug, Procedure).")
        .Produces<IReadOnlyList<TopicTypeSummary>>()
        .WithTags("Topics");

        app.MapGet("/topics/categories", [RequiresApiKey] async (ITopicRepository repo, CancellationToken ct) =>
        {
            var categories = await repo.GetDistinctCategoriesAsync(ct);
            return Results.Ok(categories);
        })
        .WithName("ListTopicCategories")
        .WithSummary("List available topic categories")
        .WithDescription("Returns a list of distinct medical categories currently in the database (e.g. Infectious & Parasitic Diseases, Nervous System). Categories group topics by body system and content type, independently of topic type.")
        .Produces<IReadOnlyList<TopicCategorySummary>>()
        .WithTags("Topics");

        app.MapGet("/topics/changes", [RequiresApiKey] async (string? since, int? skip, int? take, int? page, int? pageSize, ITopicRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(since) || !DateTime.TryParse(since, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var sinceUtc))
                return Results.Problem(
                    detail: "`since` is required and must be a valid ISO 8601 date/time (e.g. 2026-08-01T00:00:00Z).",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest);

            var (s, t) = ResolvePaging(skip, take, page, pageSize);

            var changes = await repo.GetChangedSinceAsync(sinceUtc, s, t, ct);
            var totalCount = await repo.GetChangedSinceCountAsync(sinceUtc, ct);
            return Results.Ok(new PaginatedResponse<TopicChangeItem>(changes, totalCount, s, t));
        })
        .WithName("ListTopicChanges")
        .WithSummary("List topics added or updated since a given time")
        .WithDescription("Returns topics added or updated since the given `since` timestamp (ISO 8601, e.g. 2026-08-01T00:00:00Z), most recent first. `changeType` is \"Added\" for topics never reprocessed since creation, otherwise \"Updated\" — an approximation, not an exact add/update log. Removed topics are not currently tracked by this endpoint.")
        .Produces<PaginatedResponse<TopicChangeItem>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithTags("Topics");

        app.MapGet("/topics/search", [RequiresApiKey] async (string? query, int? skip, int? take, int? page, int? pageSize, string? type, string? category, ITopicRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > ApiConstants.MaxQueryLength)
                return Results.Problem(
                    detail: $"Search query must be between 1 and {ApiConstants.MaxQueryLength} characters.",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest);

            var (s, t) = ResolvePaging(skip, take, page, pageSize);

            var results = await repo.SearchAsync(query, s, t, type, category, ct);
            var totalCount = await repo.SearchCountAsync(query, type, category, ct);
            return Results.Ok(new PaginatedResponse<TopicListItem>(results, totalCount, s, t));
        })
        .WithName("SearchTopics")
        .WithSummary("Search topics")
        .WithDescription("Searches health topics by name using a partial match. Use `skip` and `take` (or `page` and `pageSize`) to paginate results (default: 50 per page, max: 200). Use `type` to filter by topic type (e.g. Disease, Drug) and/or `category` to filter by medical category (e.g. Nervous System).")
        .Produces<PaginatedResponse<TopicListItem>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithTags("Topics");

        app.MapGet("/topics/icd11/{code}", [RequiresApiKey] async (string code, ITopicRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > ApiConstants.MaxQueryLength)
                return Results.Problem(
                    detail: $"ICD-11 code must be between 1 and {ApiConstants.MaxQueryLength} characters.",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest);

            var topic = await repo.GetByIcd11CodeAsync(code, ct);

            if (topic is null)
                return Results.Problem(
                    detail: "No topic found with that ICD-11 code.",
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(topic);
        })
        .WithName("GetTopicByIcd11Code")
        .WithSummary("Get a topic by its ICD-11 code")
        .WithDescription("Returns the health topic matching the given WHO ICD-11 code (e.g. \"5A11\"), the reverse of the icd11Code field returned on every topic. Only topics with a confidently-matched ICD-11 code can be found this way -- not every topic has one."+" The three lists are named by topic type on the wire (diseases: symptoms, causes, treatments; drugs: sideEffects, contraindications, indications; see https://meducateapi.com/health-topics-api for all types). The schema below shows the internal names observations, factors and actions instead.")
        .Produces<HealthTopic>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Topics");

        app.MapGet("/topics/{name}", [RequiresApiKey] async (string name, ITopicRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > ApiConstants.MaxQueryLength)
                return Results.Problem(
                    detail: $"Topic name must be between 1 and {ApiConstants.MaxQueryLength} characters.",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest);

            var topic = await repo.GetByNameAsync(name, ct);

            if (topic is null)
                return Results.Problem(
                    detail: "Topic not found.",
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(topic);
        })
        .WithName("GetTopicByName")
        .WithSummary("Get a topic by name")
        .WithDescription("Returns a single health topic matching the given name, including its summary, three type-specific lists, citations, tags and ICD-11 code."+" The three lists are named by topic type on the wire (diseases: symptoms, causes, treatments; drugs: sideEffects, contraindications, indications; see https://meducateapi.com/health-topics-api for all types). The schema below shows the internal names observations, factors and actions instead.")
        .Produces<HealthTopic>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Topics");

        app.MapGet("/topics", [RequiresApiKey] async (int? skip, int? take, int? page, int? pageSize, string? type, string? category, ITopicRepository repo, CancellationToken ct) =>
        {
            var (s, t) = ResolvePaging(skip, take, page, pageSize);

            var topics = await repo.GetAllAsync(s, t, type, category, ct);
            var totalCount = await repo.GetCountAsync(type, category, ct);
            return Results.Ok(new PaginatedResponse<TopicListItem>(topics, totalCount, s, t));
        })
        .WithName("ListTopics")
        .WithSummary("List all topics")
        .WithDescription("Returns a paginated list of all health topics. Use `skip` and `take` (or `page` and `pageSize`) to paginate (default: 50 per page, max: 200). Use `type` to filter by topic type (e.g. Disease, Drug) and/or `category` to filter by medical category (e.g. Nervous System).")
        .Produces<PaginatedResponse<TopicListItem>>()
        .WithTags("Topics");

        return app;
    }

    // Accepts either skip/take or page/pageSize (1-based) — the response body's own
    // pagination metadata uses "page"/"pageSize" terminology, so callers who mirror
    // that back as request params should work rather than silently no-op.
    private static (int Skip, int Take) ResolvePaging(int? skip, int? take, int? page, int? pageSize)
    {
        var t = (take ?? pageSize) switch
        {
            null or <= 0 => ApiConstants.DefaultPageSize,
            > ApiConstants.MaxPageSize => ApiConstants.MaxPageSize,
            var v => v.Value
        };

        var s = page is > 0
            ? (page.Value - 1) * t
            : skip is null or < 0 ? 0 : skip.Value;

        return (s, t);
    }
}
