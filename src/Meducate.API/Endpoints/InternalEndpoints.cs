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
        ["backup-user-data"]           = c => c.Enqueue<BackupJob>(j => j.ExecuteAsync(JobCancellationToken.Null, null)),
    };

    internal static WebApplication MapInternalEndpoints(this WebApplication app)
    {
        // Operational routes: keep them out of the public OpenAPI document.
        var internalRoutes = app.MapGroup(string.Empty).ExcludeFromDescription();
        internalRoutes.MapPost("/internal/jobs/{jobName}", Handle);
        internalRoutes.MapGet("/internal/jobs/{jobName}/last-run", HandleLastRun);
        internalRoutes.MapGet("/internal/topics/sample",
            (HttpContext http, ITopicQueryRepository queryRepo, IConfiguration config, ILoggerFactory loggerFactory, int count = 10, int? skip = null, CancellationToken ct = default)
                => HandleTopicSample(http, queryRepo, config, loggerFactory, count, skip, ct));
        internalRoutes.MapGet("/internal/waitlist",
            (HttpContext http, IWaitlistRepository waitlist, IConfiguration config, ILoggerFactory loggerFactory, int take = 50, int skip = 0, CancellationToken ct = default)
                => HandleWaitlistList(http, waitlist, config, loggerFactory, take, skip, ct));
        internalRoutes.MapPost("/internal/topics/reprocess",
            (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, [Microsoft.AspNetCore.Mvc.FromQuery] string[] type, [Microsoft.AspNetCore.Mvc.FromQuery] string[] name, CancellationToken ct)
                => HandleTopicReprocess(http, writeRepo, config, loggerFactory, type, name, ct));
        internalRoutes.MapPost("/internal/topics/resync",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "resync");
                if (authError is not null) return authError;

                var count = await writeRepo.ForceSourceRefreshAsync(ct);
                logger.LogInformation("Internal: {Count} topics will re-fetch their source on the next refresh", count);
                return Results.Ok(new { topics = count });
            });
        internalRoutes.MapGet("/internal/backup/export",
            async (HttpContext http, Meducate.Domain.Services.IDataBackupService backup, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var authError = CheckToken(http, config, loggerFactory.CreateLogger("Meducate.API.Internal"), "backup-export");
                return authError ?? Results.Ok(await backup.ExportAsync(ct));
            });
        internalRoutes.MapPost("/internal/backup/restore",
            async (HttpContext http, Meducate.Domain.Services.IDataBackupService backup, IConfiguration config, ILoggerFactory loggerFactory, Meducate.Domain.Services.BackupDocument document, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "backup-restore");
                if (authError is not null) return authError;

                var result = await backup.RestoreAsync(document, ct);
                logger.LogInformation("Internal: restored backup ({Users} users, {Orgs} organisations, {Waitlist} waitlist entries added)", result.UsersAdded, result.OrganisationsAdded, result.WaitlistAdded);
                return Results.Ok(result);
            });
        internalRoutes.MapGet("/internal/llm/spend",
            async (HttpContext http, Meducate.Domain.Services.ILlmSpendTracker tracker, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct, int days = 7) =>
            {
                var authError = CheckToken(http, config, loggerFactory.CreateLogger("Meducate.API.Internal"), "llm-spend");
                if (authError is not null) return authError;

                var summary = await tracker.GetSummaryAsync(Math.Clamp(days, 1, 60), ct);
                return Results.Ok(new
                {
                    dailyBudgetUsd = summary.DailyBudgetUsd,
                    spentTodayUsd = Math.Round(summary.SpentTodayUsd, 4),
                    today = summary.TodayByModel.Select(m => new { m.Model, m.Calls, m.InputTokens, m.OutputTokens, costUsd = Math.Round(m.CostUsd, 4) }),
                    days = summary.Days.Select(d => new { d.Day, d.Calls, costUsd = Math.Round(d.CostUsd, 4) }),
                });
            });
        internalRoutes.MapGet("/internal/topics/stats",
            async (HttpContext http, ITopicQueryRepository queryRepo, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var authError = CheckToken(http, config, loggerFactory.CreateLogger("Meducate.API.Internal"), "stats");
                if (authError is not null) return authError;

                var total = (await queryRepo.GetAllTopicNamesAsync(ct)).Count;
                var uncategorized = await queryRepo.GetUncategorizedTopicsAsync(ct);
                return Results.Ok(new
                {
                    rows = total,
                    served = await queryRepo.GetServedTopicCountAsync(ct),
                    seen = (await queryRepo.GetAllSeenTopicNamesAsync(ct)).Count,
                    uncategorized = uncategorized.Select(t => new { t.Name, t.TopicType, t.NeedsLlmReprocessing }),
                });
            });
        internalRoutes.MapPost("/internal/topics/edit-items",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, List<Meducate.Domain.Services.ItemEdit> edits, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "edit-items");
                if (authError is not null) return authError;

                var (applied, notFound) = await writeRepo.EditListItemsAsync(edits, ct);
                logger.LogInformation("Internal: edited list items ({Applied} applied, {NotFound} not found)", applied, notFound);
                return Results.Ok(new { applied, notFound });
            });
        internalRoutes.MapGet("/internal/seen",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, string name, CancellationToken ct) =>
            {
                var authError = CheckToken(http, config, loggerFactory.CreateLogger("Meducate.API.Internal"), "seen");
                if (authError is not null) return authError;

                var rows = await writeRepo.FindSeenTopicsAsync(name, ct);
                return Results.Ok(rows.Select(s => new { s.Name, s.Status, s.TopicType, s.FirstSeen }));
            });
        internalRoutes.MapPost("/internal/seen/forget",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, string[] names, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "forget");
                if (authError is not null) return authError;

                var removed = await writeRepo.ForgetSeenTopicsAsync(names, ct);
                logger.LogInformation("Internal: forgot {Count} seen topic names so discovery can pick them up again", removed);
                return Results.Ok(new { removed });
            });
        internalRoutes.MapPost("/internal/topics/categorize",
            async (HttpContext http, ITopicWriteRepository writeRepo, Meducate.Domain.Services.ILLMProcessor llm, IConfiguration config, ILoggerFactory loggerFactory, Dictionary<string, string> nameToCategory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "categorize");
                if (authError is not null) return authError;

                var valid = llm.GetValidCategories();
                var invalid = nameToCategory.Values.Where(c => !valid.Contains(c)).Distinct().ToList();
                if (invalid.Count > 0)
                    return Results.Problem($"Unknown categories: {string.Join(", ", invalid)}.", statusCode: StatusCodes.Status400BadRequest);

                var results = new Dictionary<string, int>();
                foreach (var (name, category) in nameToCategory)
                    results[name] = await writeRepo.SetCategoryAsync(name, category, ct);

                logger.LogInformation("Internal: set categories on {Count} topics", results.Count(r => r.Value > 0));
                return Results.Ok(results);
            });
        internalRoutes.MapPost("/internal/topics/recode",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "recode");
                if (authError is not null) return authError;

                var cleared = await writeRepo.ClearIcd11CodesAsync(ct);
                logger.LogInformation("Internal: cleared {Count} ICD-11 codes; the next refresh looks them up again", cleared);
                return Results.Ok(new { cleared });
            });
        internalRoutes.MapPost("/internal/topics/sanitize",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "sanitize");
                if (authError is not null) return authError;

                var changed = await writeRepo.SanitizeAllAsync(ct);
                logger.LogInformation("Internal: sanitised {Count} stored topics", changed);
                return Results.Ok(new { changed });
            });
        internalRoutes.MapGet("/internal/diag/client",
            (HttpContext http, IConfiguration config, ILoggerFactory loggerFactory) =>
            {
                var authError = CheckToken(http, config, loggerFactory.CreateLogger("Meducate.API.Internal"), "diag");
                if (authError is not null) return authError;

                // What the app sees of the caller: used to confirm which address the rate limiters key on.
                return Results.Ok(new
                {
                    resolved = Meducate.API.Infrastructure.ClientIp.Resolve(http),
                    remote = http.Connection.RemoteIpAddress?.ToString(),
                    scheme = http.Request.Scheme,
                    cfConnectingIp = http.Request.Headers["CF-Connecting-IP"].ToString(),
                    xForwardedFor = http.Request.Headers["X-Forwarded-For"].ToString(),
                    xRealIp = http.Request.Headers["X-Real-IP"].ToString(),
                });
            });
        internalRoutes.MapPost("/internal/topics/rename",
            async (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, List<RenameRequest> renames, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Meducate.API.Internal");
                var authError = CheckToken(http, config, logger, "rename");
                if (authError is not null) return authError;

                var results = new List<object>();
                foreach (var r in renames)
                {
                    var type = RetypeTargets.FirstOrDefault(t => string.Equals(t, r.Type, StringComparison.OrdinalIgnoreCase));
                    if (type is null || string.IsNullOrWhiteSpace(r.To))
                    {
                        results.Add(new { r.From, error = "Invalid target name or type." });
                        continue;
                    }

                    try
                    {
                        results.Add(new { r.From, r.To, updated = await writeRepo.RenameAndRetypeAsync(r.From, r.To.Trim(), type, ct) });
                    }
                    catch (Exception ex)
                    {
                        // Typically a unique-name collision; the caller reorders and retries.
                        results.Add(new { r.From, r.To, error = ex.GetBaseException().Message });
                    }
                }

                logger.LogInformation("Internal: renamed/retyped {Count} topics", results.Count);
                return Results.Ok(results);
            });
        internalRoutes.MapPost("/internal/topics/retype",
            (HttpContext http, ITopicWriteRepository writeRepo, IConfiguration config, ILoggerFactory loggerFactory, Dictionary<string, string> nameToType, CancellationToken ct)
                => HandleTopicRetype(http, writeRepo, config, loggerFactory, nameToType, ct));
        internalRoutes.MapGet("/internal/topics/fact-check",
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

    // Body item for /internal/topics/rename: restore a topic's provider title and correct its type.
    internal sealed record RenameRequest(string From, string To, string Type);

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
        rawSourceChars = Math.Clamp(rawSourceChars, 500, 40000);

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
