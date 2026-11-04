using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Meducate.Application.Jobs;
using Meducate.Domain.Repositories;

namespace Meducate.API.Endpoints;

internal static class InternalEndpoints
{
    private static readonly Dictionary<string, Action<IBackgroundJobClient>> KnownJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["data-integrity-check"]        = c => c.Enqueue<DataIntegrityCheckJob>(j => j.ExecuteAsync(JobCancellationToken.Null, null)),
        ["refresh-medical-conditions"]  = c => c.Enqueue<TopicRefreshJob>(j => j.ExecuteAsync(JobCancellationToken.Null, null)),
        ["discover-medical-conditions"] = c => c.Enqueue<TopicDiscoveryJob>(j => j.ExecuteAsync(JobCancellationToken.Null, null)),
    };

    internal static WebApplication MapInternalEndpoints(this WebApplication app)
    {
        app.MapPost("/internal/jobs/{jobName}", Handle);
        app.MapGet("/internal/jobs/{jobName}/last-run", HandleLastRun);
        app.MapGet("/internal/topics/sample",
            (HttpContext http, ITopicQueryRepository queryRepo, IConfiguration config, ILoggerFactory loggerFactory, int count = 10, int? skip = null, CancellationToken ct = default)
                => HandleTopicSample(http, queryRepo, config, loggerFactory, count, skip, ct));
        app.MapGet("/internal/waitlist",
            (HttpContext http, IWaitlistRepository waitlist, IConfiguration config, ILoggerFactory loggerFactory, int take = 50, int skip = 0, CancellationToken ct = default)
                => HandleWaitlistList(http, waitlist, config, loggerFactory, take, skip, ct));
        app.MapPost("/internal/topics/reprocess",
            (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, [Microsoft.AspNetCore.Mvc.FromQuery] string[] type, [Microsoft.AspNetCore.Mvc.FromQuery] string[] name, CancellationToken ct)
                => HandleTopicReprocess(http, writeRepo, config, loggerFactory, type, name, ct));
        app.MapPost("/internal/topics/retype",
            (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, Dictionary<string, string> nameToType, CancellationToken ct)
                => HandleTopicRetype(http, writeRepo, config, loggerFactory, nameToType, ct));
        app.MapGet("/internal/topics/fact-check",
            (HttpContext http, ITopicQueryRepository queryRepo, IConfiguration config, ILoggerFactory loggerFactory, int count = 5, int? skip = null, int rawSourceChars = 3000, CancellationToken ct = default)
                => HandleTopicFactCheck(http, queryRepo, config, loggerFactory, count, skip, rawSourceChars, ct));
        return app;
    }

    private static IResult? CheckToken(HttpContext http, IConfiguration config, ILogger logger, string context)
    {
        var expectedToken = config["Internal:TriggerToken"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            logger.LogWarning("Internal endpoint called but Internal:TriggerToken is not configured");
            return Results.Problem("Internal endpoint is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var provided = http.Request.Headers["X-Internal-Token"].FirstOrDefault() ?? "";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        var tokenValid = providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);

        if (!tokenValid)
        {
            logger.LogWarning("Internal endpoint: rejected request ({Context}) — bad token", context);
            return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return null; // authorized
    }

    private static IResult Handle(
        string jobName,
        HttpContext http,
        IBackgroundJobClient jobClient,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, $"trigger {jobName}");
        if (authError is not null) return authError;

        if (!KnownJobs.TryGetValue(jobName, out var enqueue))
        {
            logger.LogWarning("Internal trigger: unknown job '{JobName}'", jobName);
            return Results.Problem($"Unknown job '{jobName}'.", statusCode: StatusCodes.Status404NotFound);
        }

        enqueue(jobClient);
        logger.LogInformation("Internal trigger: enqueued job '{JobName}'", jobName);
        return Results.Accepted(value: new { job = jobName, status = "enqueued" });
    }

    private static IResult HandleLastRun(
        string jobName,
        HttpContext http,
        JobResultStore resultStore,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, $"last-run {jobName}");
        if (authError is not null) return authError;

        var result = resultStore.Get(jobName);
        if (result is null)
            return Results.Problem(
                $"No result available for '{jobName}'. Either the job has not run since the last deployment, or the job name is unknown.",
                statusCode: StatusCodes.Status404NotFound);

        return Results.Ok(new
        {
            job          = jobName,
            ranAt        = result.RanAt,
            durationMs   = result.DurationMs,
            batchIndex   = result.BatchIndex,
            totalBatches = result.TotalBatches,
            topicsChecked = result.TopicsChecked,
            failures     = result.Failures,
            warnings     = result.Warnings,
            failureDetails = result.FailureDetails,
        });
    }

    private static async Task<IResult> HandleTopicSample(
        HttpContext http,
        ITopicQueryRepository queryRepo,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        int count = 10,
        int? skip = null,
        CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, "topics/sample");
        if (authError is not null) return authError;

        count = Math.Clamp(count, 1, 50);

        var total = await queryRepo.GetServedTopicCountAsync(ct);
        if (total == 0)
            return Results.Problem("No served topics found.", statusCode: StatusCodes.Status404NotFound);

        var maxSkip = Math.Max(0, total - count);
        var resolvedSkip = skip.HasValue ? Math.Clamp(skip.Value, 0, maxSkip) : Random.Shared.Next(0, maxSkip + 1);
        var topics = await queryRepo.GetServedTopicBatchAsync(resolvedSkip, count, ct);

        var result = topics.Select(t => new
        {
            t.Name,
            t.Category,
            t.TopicType,
            t.Summary,
            t.LastUpdated,
            hasObservations = t.Observations?.Count > 0,
            hasFactor       = t.Factors?.Count > 0,
            hasActions      = t.Actions?.Count > 0,
            citationCount   = t.Citations?.Count ?? 0,
            rawSourceLength = t.RawSource?.Length ?? 0,
            rawSourcePreview = t.RawSource is { Length: > 0 }
                ? t.RawSource[..Math.Min(300, t.RawSource.Length)]
                : null,
        });

        return Results.Ok(new { total, skip = resolvedSkip, count = topics.Count, topics = result });
    }

    private static readonly HashSet<string> RetypeTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Disease", "Disorder", "Syndrome", "Symptom", "Mental Health", "Drug", "Procedure",
        "Diagnostic Test", "Vaccine", "Anatomy", "Nutrient",
    };

    // Body: { "Topic Name": "NewType", ... }. Fixes classifier mistakes; the topic is re-categorised,
    // re-coded and re-extracted on the next refresh run (and is hidden from the API until categorised).
    private static async Task<IResult> HandleTopicRetype(
        HttpContext http,
        ITopicWriteRepository writeRepo,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        Dictionary<string, string> nameToType,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, "retype");
        if (authError is not null) return authError;

        var bad = nameToType.Values.Where(t => !RetypeTargets.Contains(t)).Distinct().ToList();
        if (bad.Count > 0)
            return Results.Problem($"Unknown type(s): {string.Join(", ", bad)}.", statusCode: StatusCodes.Status400BadRequest);

        var results = new Dictionary<string, int>();
        foreach (var (name, type) in nameToType)
        {
            var canonical = RetypeTargets.First(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
            results[name] = await writeRepo.RetypeAsync(name, canonical, ct);
        }

        logger.LogInformation("Internal: retyped {Count} topics: {Names}", results.Count(r => r.Value > 0), string.Join(", ", results.Keys));
        return Results.Ok(results);
    }

    // Flags every topic of the given type(s) for LLM reprocessing on the next refresh run.
    // Use after changing extraction prompts, since unchanged sources are otherwise never re-extracted.
    private static async Task<IResult> HandleTopicReprocess(
        HttpContext http,
        ITopicWriteRepository writeRepo,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        string[] type,
        string[] name,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, "reprocess");
        if (authError is not null) return authError;

        if (type.Length == 0 && name.Length == 0)
            return Results.Problem("Pass at least one ?type= or ?name=.", statusCode: StatusCodes.Status400BadRequest);

        var flagged = await writeRepo.FlagForReprocessingAsync(type, name, ct);
        logger.LogInformation("Internal: flagged {Count} topics (types: {Types}; names: {Names}) for reprocessing", flagged, string.Join(", ", type), string.Join(", ", name));
        return Results.Ok(new { flagged, types = type, names = name });
    }

    private static async Task<IResult> HandleWaitlistList(
        HttpContext http,
        IWaitlistRepository waitlist,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        int take,
        int skip,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, "waitlist");
        if (authError is not null) return authError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var total = await waitlist.CountAsync(ct);
        var entries = await waitlist.ListAsync(skip, take, ct);

        return Results.Ok(new
        {
            total,
            skip,
            count = entries.Count,
            entries = entries.Select(e => new { e.Email, e.CreatedAt }),
        });
    }

    private static async Task<IResult> HandleTopicFactCheck(
        HttpContext http,
        ITopicQueryRepository queryRepo,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        int count = 5,
        int? skip = null,
        int rawSourceChars = 3000,
        CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("Meducate.API.Internal");

        var authError = CheckToken(http, config, logger, "topics/fact-check");
        if (authError is not null) return authError;

        count = Math.Clamp(count, 1, 10);
        rawSourceChars = Math.Clamp(rawSourceChars, 500, 10000);

        var total = await queryRepo.GetServedTopicCountAsync(ct);
        if (total == 0)
            return Results.Problem("No served topics found.", statusCode: StatusCodes.Status404NotFound);

        var maxSkip = Math.Max(0, total - count);
        var resolvedSkip = skip.HasValue ? Math.Clamp(skip.Value, 0, maxSkip) : Random.Shared.Next(0, maxSkip + 1);
        var topics = await queryRepo.GetServedTopicBatchAsync(resolvedSkip, count, ct);

        var result = topics.Select(t => new
        {
            t.Name,
            t.OriginalName,
            t.Category,
            t.TopicType,
            t.Icd11Code,
            t.Version,
            t.LastUpdated,
            t.ReprocessAttempts,
            t.Summary,
            observations = t.Observations ?? [],
            factors      = t.Factors ?? [],
            actions      = t.Actions ?? [],
            citations    = t.Citations ?? [],
            tags         = t.Tags ?? [],
            rawSourceLength  = t.RawSource?.Length ?? 0,
            rawSource = t.RawSource is { Length: > 0 }
                ? t.RawSource[..Math.Min(rawSourceChars, t.RawSource.Length)]
                : null,
        });

        return Results.Ok(new { total, skip = resolvedSkip, count = topics.Count, topics = result });
    }
}
