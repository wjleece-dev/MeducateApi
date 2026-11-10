using Hangfire.Console;
using Hangfire.Server;
using Meducate.Application.Helpers;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Services;

internal sealed class TopicIngestionService(
    IEnumerable<IMedicalDataProvider> providers,
    ITopicQueryRepository queryRepo,
    ITopicWriteRepository writeRepo,
    ILLMProcessor llmProcessor,
    ITopicRepository topicRepo,
    TopicBackfillService backfillService,
    ILogger<TopicIngestionService> logger)
{
    private readonly IMedicalDataProvider[] _providers = [.. providers];
    private readonly ITopicQueryRepository _queryRepo = queryRepo;
    private readonly ITopicWriteRepository _writeRepo = writeRepo;
    private readonly ILLMProcessor _llmProcessor = llmProcessor;
    private readonly ITopicRepository _topicRepo = topicRepo;
    private readonly TopicBackfillService _backfillService = backfillService;
    private readonly ILogger<TopicIngestionService> _logger = logger;

    private static readonly TimeSpan LlmThrottle = TimeSpan.FromMilliseconds(500);

    internal async Task IngestAsync(PerformContext? console = null, CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Starting medical data ingestion from {Count} providers", _providers.Length);

        console?.WriteLine($"Starting discovery from {_providers.Length} providers...");

        // 1. Load seen topic names — providers filter against these to skip already-classified topics
        var seenNamesSet = await _queryRepo.GetAllSeenTopicNamesAsync(ct);

        // Also load existing HealthTopic names for synonym collision checks later
        var existingNames = await _queryRepo.GetAllTopicNamesAsync(ct);
        var existingNamesSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        // Merge both sets for provider filtering — skip anything we've already seen or have
        var filterSet = new HashSet<string>(seenNamesSet, StringComparer.OrdinalIgnoreCase);
        filterSet.UnionWith(existingNamesSet);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Loaded {SeenCount} seen topics and {ExistingCount} existing topics from DB",
                seenNamesSet.Count, existingNames.Count);

        console?.WriteLine($"Loaded {seenNamesSet.Count} seen + {existingNames.Count} existing topics from DB.");

        // 2. Discover new topics from all providers in parallel, passing seen+existing names
        console?.WriteLine("Discovering new health topics from providers...");
        var discoveryTasks = _providers.Select(p => DiscoverSafeAsync(p, filterSet, ct));
        var discoveryResults = await Task.WhenAll(discoveryTasks);
        var allDiscoveries = discoveryResults.SelectMany(r => r).ToList();

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Total raw discoveries: {Count}", allDiscoveries.Count);

        console?.WriteLine($"Total raw discoveries: {allDiscoveries.Count}");

        // 3. LLM classify — assign a TopicType to each new topic (with summary context)
        var uniqueCandidates = allDiscoveries
            .GroupBy(d => d.TopicName, StringComparer.OrdinalIgnoreCase)
            .Where(g => !existingNamesSet.Contains(g.Key))
            .Select(g => new TopicClassifyInput(g.Key, g.First().RawText))
            .ToList();

        var topicTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (uniqueCandidates.Count > 0)
        {
            console?.WriteLine($"Classifying {uniqueCandidates.Count} topics by type...");
            try
            {
                topicTypeMap = await _llmProcessor.ClassifyTopicNamesAsync(uniqueCandidates, ct);

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation(
                        "LLM classify: {Count} topics classified into types: {Types}",
                        topicTypeMap.Count,
                        string.Join(", ", topicTypeMap.Values.GroupBy(v => v).Select(g => $"{g.Key}={g.Count()}")));

                console?.WriteLine($"Classified {topicTypeMap.Count} topics.");
            }
            catch (Exception ex)
            {
                // Defaulting to 'Other' would record every candidate in SeenTopic as unclassifiable and block it
                // from rediscovery for good, so an unavailable classifier (quota, outage) ends the run instead.
                _logger.LogWarning(ex, "LLM topic classification failed; ending the discovery run so nothing is recorded as unclassifiable");
                console?.WriteLine($"LLM classify failed: {ex.Message} — ending the run, will retry next time.");
                return;
            }
        }

        // 3b. Record filtered/non-medical classification decisions in SeenTopic table immediately.
        //     Processable types (Disease, Disorder, etc.) are recorded only after successful
        //     HealthTopic creation in step 5 — recording them here would permanently block
        //     rediscovery if LLM processing later fails.
        if (topicTypeMap.Count > 0)
        {
            var seenTopics = topicTypeMap
                .Where(kvp => !_llmProcessor.ShouldProcessTopicType(kvp.Value))
                .Select(kvp => new SeenTopic
                {
                    Name = kvp.Key,
                    Status = TopicHelpers.GetSeenStatus(kvp.Value),
                    TopicType = kvp.Value,
                    FirstSeen = DateTime.UtcNow
                }).ToList();

            if (seenTopics.Count > 0)
                await _writeRepo.AddSeenTopicsAsync(seenTopics, ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Recorded {Count} filtered classification decisions in SeenTopics", seenTopics.Count);

            console?.WriteLine($"Recorded {seenTopics.Count} filtered classification decisions.");
        }

        // 4. Group discoveries by topic name, merge raw text across providers — all topics proceed
        var newByName = allDiscoveries
            .Where(d => !existingNamesSet.Contains(d.TopicName))
            .GroupBy(d => d.TopicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Found {Count} new health topics to process", newByName.Count);

        console?.WriteLine($"Processing {newByName.Count} new health topics...");

        // 5. Process new topics — batch save every 10 for durability
        var addedCount = 0;
        foreach (var group in newByName)
        {
            try
            {
                // Skip if this name was added by a previous iteration in this loop
                if (existingNamesSet.Contains(group.Key))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Skipping '{Name}' — already added this run", group.Key);

                    continue;
                }

                var mergedRawSource = TopicHelpers.BuildMergedRawSource(group);
                var topicType = topicTypeMap.GetValueOrDefault(group.Key, TopicConstants.TopicTypeOther);

                if (string.Equals(topicType, TopicConstants.TopicTypeNonMedical, StringComparison.OrdinalIgnoreCase))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Skipping '{Name}' — classified as Non-Medical", group.Key);

                    console?.WriteLine($"  [{group.Key}] Skipped — non-medical topic.");

                    continue;
                }

                if (string.Equals(topicType, TopicConstants.TopicTypeOther, StringComparison.OrdinalIgnoreCase))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Skipping '{Name}' — classified as Other (unclassifiable)", group.Key);

                    console?.WriteLine($"  [{group.Key}] Skipped — unclassifiable topic.");

                    continue;
                }

                if (!_llmProcessor.ShouldProcessTopicType(topicType))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Skipping '{Name}' — filtered type: {Type}", group.Key, topicType);

                    console?.WriteLine($"  [{group.Key}] Filtered — {topicType} topics are excluded.");

                    continue;
                }

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Processing '{Name}' (type: {Type}) — source length: {Length} chars",
                        group.Key, topicType, mergedRawSource.Length);

                console?.WriteLine($"  [{group.Key}] type: {topicType}, source: {mergedRawSource.Length} chars");

                if (mergedRawSource.Length < TopicConstants.MinSourceLength)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Skipping '{Name}' — source too short ({Length} chars)",
                            group.Key, mergedRawSource.Length);

                    console?.WriteLine($"  [{group.Key}] Skipped — source too short.");

                    continue;
                }

                await Task.Delay(LlmThrottle, ct);
                var structured = await _llmProcessor.ParseHealthTopicAsync(mergedRawSource, topicType, group.Key, ct);

                // Skip verification when extraction is structurally broken (nothing to verify); semantic
                // issues such as aftercare items are exactly what the verifier is there to fix
                if (structured is not null && TopicHelpers.IsVerifiable(TopicHelpers.CheckTopicQuality(structured)))
                {
                    await Task.Delay(LlmThrottle, ct);
                    structured = await _llmProcessor.VerifyHealthTopicAsync(mergedRawSource, structured, ct) ?? structured;

                    // Last gate: drop list items the source does not back (quote-checked, see ItemGrounding)
                    await Task.Delay(LlmThrottle, ct);
                    var grounding = ItemGrounding.Apply(structured, mergedRawSource,
                        await _llmProcessor.JudgeListItemsAsync(mergedRawSource, structured, ct));
                    if (grounding.Dropped.Count > 0 || grounding.Rejected is not null)
                        _logger.LogInformation("Grounding '{Name}': dropped {Count} unsupported items{Rejected}",
                            group.Key, grounding.Dropped.Count, grounding.Rejected is null ? "" : $" ({grounding.Rejected})");
                }

                if (structured is null)
                {
                    _logger.LogWarning("LLM failed to extract structured data for '{Name}' (type: {Type})", group.Key, topicType);

                    console?.WriteLine($"  [{group.Key}] Failed — LLM could not extract structured data.");

                    continue;
                }

                // Synonym collision: LLM might normalise "High Blood Pressure" → "Hypertension"
                if (existingNamesSet.Contains(structured.Name))
                {
                    var existing = await _queryRepo.GetByNameTrackedAsync(structured.Name, ct);

                    if (existing is null)
                    {
                        _logger.LogWarning("Synonym collision: '{Discovered}' resolved to '{Resolved}' which is in the name set but not found in DB — skipping",
                            group.Key, structured.Name);

                        continue;
                    }

                    // Ask the LLM whether the discovered name is broader/better
                    var comparison = await _llmProcessor.CompareBroaderNameAsync(group.Key, existing.Name, ct);

                    if (comparison.ShouldReplace)
                    {
                        // LLM determined the candidate is the better/broader name
                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation(
                                "Renaming '{Existing}' to '{Preferred}' (LLM determined broader) and merging source data",
                                existing.Name, comparison.PreferredName);

                        console?.WriteLine($"  [{group.Key}] Renamed '{existing.Name}' → '{comparison.PreferredName}' (broader term).");

                        existing.Name = TopicHelpers.ToTitleCase(comparison.PreferredName);
                        existing.OriginalName ??= group.Key;
                        var mergedSource = TopicHelpers.BuildMergedRawSource(group) +
                            (existing.RawSource is not null ? "\n---\n" + existing.RawSource : "");
                        existing.RawSource = mergedSource;
                        existing.SourceHash = ContentHasher.GetSourceHash(group, mergedSource);
                        existing.NeedsLlmReprocessing = true;
                        existing.LastSourceRefresh = DateTime.UtcNow;
                        existingNamesSet.Add(comparison.PreferredName);
                        await _writeRepo.SaveChangesAsync(ct);
                        continue;
                    }

                    if (string.Equals(comparison.PreferredName, group.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        // LLM says they're different subjects — the parser's own normalization
                        // collapsed the candidate's name into the existing entry (e.g. "A1C" the
                        // test parsed to "Type 2 Diabetes"), not a true synonym. Keep it as its
                        // own topic under the originally-discovered name instead of dropping it.
                        var disambiguatedName = TopicHelpers.ToTitleCase(group.Key);
                        if (existingNamesSet.Contains(disambiguatedName))
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                                _logger.LogInformation(
                                    "Skipping '{Discovered}' — different subject from '{Existing}' but original name also taken",
                                    group.Key, existing.Name);

                            console?.WriteLine($"  [{group.Key}] Skipped — different subject from '{existing.Name}', name also taken.");
                            continue;
                        }

                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation(
                                "'{Discovered}' is a different subject from '{Existing}' — keeping as separate topic '{Name}'",
                                group.Key, existing.Name, disambiguatedName);

                        console?.WriteLine($"  [{group.Key}] Different subject from '{existing.Name}' — keeping as '{disambiguatedName}'.");
                        structured.Name = disambiguatedName;
                        // Falls through to the normal save path below.
                    }
                    else
                    {
                        // Same subject, existing name (or a normalized form) is preferred — merge source data
                        existing.OriginalName ??= group.Key;
                        var newSource = TopicHelpers.BuildMergedRawSource(group);
                        if (existing.RawSource is null || !existing.RawSource.Contains(newSource))
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                                _logger.LogInformation(
                                    "Merging new source data into existing '{Name}' from synonym '{Discovered}'",
                                    existing.Name, group.Key);

                            console?.WriteLine($"  [{group.Key}] Merged source data into existing '{existing.Name}'.");

                            var combined = existing.RawSource is not null
                                ? existing.RawSource + "\n---\n" + newSource
                                : newSource;
                            existing.RawSource = combined;
                            existing.SourceHash = ContentHasher.GetSourceHash(group, combined);
                            existing.NeedsLlmReprocessing = true;
                            existing.LastSourceRefresh = DateTime.UtcNow;
                            await _writeRepo.SaveChangesAsync(ct);
                        }
                        else
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                                _logger.LogInformation(
                                    "Skipping '{Discovered}' — resolved to existing '{Resolved}', no new source data",
                                    group.Key, structured.Name);

                            console?.WriteLine($"  Skipped '{group.Key}' — resolved to existing '{structured.Name}', no new data.");
                        }

                        continue;
                    }
                }

                var qualityIssue = TopicHelpers.CheckTopicQuality(structured);
                if (qualityIssue is not null)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Saving '{Name}' with reprocessing flag — QC: {Reason}", group.Key, qualityIssue);

                    console?.WriteLine($"  [{group.Key}] Low quality — flagged for reprocessing ({qualityIssue})");

                    structured.NeedsLlmReprocessing = true;
                }

                structured.RawSource = mergedRawSource;
                structured.SourceHash = ContentHasher.GetSourceHash(group, mergedRawSource);
                structured.LastSourceRefresh = DateTime.UtcNow;
                structured.TopicType = topicType;
                structured.OriginalName = group.Key;

                await _writeRepo.AddAsync(structured, ct);
                await _writeRepo.AddSeenTopicsAsync([new SeenTopic
                {
                    Name = group.Key,
                    Status = TopicHelpers.GetSeenStatus(topicType),
                    TopicType = topicType,
                    FirstSeen = DateTime.UtcNow
                }], ct);

                existingNamesSet.Add(structured.Name);
                addedCount++;

                if (addedCount % 10 == 0)
                    await _writeRepo.SaveChangesAsync(ct);

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Added new topic: {Name} (type: {Type}, from {Sources})",
                        structured.Name, topicType, string.Join(", ", group.Select(d => d.SourceName)));

                console?.WriteLine($"  Added: {structured.Name} ({topicType})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process new topic '{Name}'", group.Key);
                console?.WriteLine($"  [{group.Key}] Failed: {ex.Message}");
            }
        }

        // Flush any remaining unsaved new topics
        if (_writeRepo.HasChanges())
            await _writeRepo.SaveChangesAsync(ct);

        // 6. Fetch all known provider names (used by backfill and stale removal)
        var allKnownNames = await FetchAllKnownNamesAsync(ct);

        // 7. Backfill OriginalName for topics that predate the OriginalName feature
        //    Must run BEFORE stale removal so the pre-filter can use populated OriginalName values
        var backfilledOriginalNames = await _backfillService.BackfillOriginalNamesAsync(allKnownNames, ct, console);

        // 8. Remove stale topics that no longer appear on any provider's index
        var removedCount = await RemoveStaleTopicsAsync(allKnownNames, existingNamesSet, console, ct);

        // 9. Backfill TopicType for any existing records that don't have one
        var backfilledCount = await _backfillService.BackfillTopicTypesAsync(ct, console);

        // 9b. Correct topics renamed incorrectly by the synonym-merge logic
        var badNamesCount = await _backfillService.BackfillBadNamesAsync(ct, console);

        // 10. Flag topics with empty structured fields for reprocessing
        var emptyFieldsCount = await _backfillService.BackfillEmptyStructuredFieldsAsync(ct, console);

        // 11. Clear bad categories so they get recategorized
        var badCategoriesCount = await _backfillService.BackfillBadCategoriesAsync(ct, console);

        // 12. Backfill categories for topics missing them (including ones just cleared above)
        var categorizedCount = await _backfillService.BackfillCategoriesAsync(ct, console);

        // 13. Remove any topics that still have no category — only categorised topics are served
        var uncategorized = await _queryRepo.GetUncategorizedTopicsAsync(ct);
        var uncategorizedRemovedCount = 0;
        // Only when the classifier answered: an outage or exhausted quota must not delete topics awaiting a category.
        if (uncategorized.Count > 0 && !_backfillService.CategoryBackfillFailed)
        {
            _logger.LogWarning("Removing {Count} topics that could not be assigned a category: {Names}",
                uncategorized.Count, string.Join(", ", uncategorized.Select(t => t.Name)));
            console?.WriteLine($"Removing {uncategorized.Count} topics with no category: {string.Join(", ", uncategorized.Select(t => t.Name))}");
            await _writeRepo.RemoveRangeAsync(uncategorized, ct);
            await _writeRepo.SaveChangesAsync(ct);
            uncategorizedRemovedCount = uncategorized.Count;
        }

        // 14. Look up ICD-11 codes for newly-categorized diagnosable topics
        var icd11Count = await _backfillService.BackfillIcd11CodesAsync(ct, console);

        if (addedCount > 0 || removedCount > 0 || backfilledCount > 0 || categorizedCount > 0
            || backfilledOriginalNames > 0 || emptyFieldsCount > 0 || badCategoriesCount > 0
            || badNamesCount > 0 || uncategorizedRemovedCount > 0 || icd11Count > 0)
            _topicRepo.InvalidateCache();

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Medical data ingestion complete — added {Added}, removed {Removed}, backfilled types {Backfilled}, backfilled originals {BackfilledOriginals}, categorized {Categorized}, flagged empty {EmptyFields}, cleared bad categories {BadCategories}, corrected names {BadNames}, removed uncategorized {Uncategorized}, ICD-11 coded {Icd11}",
                addedCount, removedCount, backfilledCount, backfilledOriginalNames, categorizedCount, emptyFieldsCount, badCategoriesCount, badNamesCount, uncategorizedRemovedCount, icd11Count);

        console?.WriteLine($"Discovery complete — added {addedCount}, removed {removedCount}, backfilled {backfilledCount} types, {backfilledOriginalNames} original names, categorized {categorizedCount}, flagged {emptyFieldsCount} empty, cleared {badCategoriesCount} bad categories, corrected {badNamesCount} bad names, ICD-11 coded {icd11Count}.");
    }

    private async Task<IReadOnlyList<RawTopicData>> DiscoverSafeAsync(
        IMedicalDataProvider provider, IReadOnlySet<string> existingNames, CancellationToken ct)
    {
        try
        {
            var found = await provider.DiscoverTopicsAsync(existingNames, ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Provider {Source} discovered {Count} topics",
                    provider.SourceName, found.Count);

            return found;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery failed for provider {Source}", provider.SourceName);
            return [];
        }
    }

    private async Task<IReadOnlySet<string>> FetchAllKnownNamesAsync(CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tasks = _providers.Select(async p =>
        {
            try { return await p.GetKnownTopicNamesAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get known names from {Source}", p.SourceName);
                return new HashSet<string>();
            }
        });

        var allSets = await Task.WhenAll(tasks);
        foreach (var set in allSets)
            result.UnionWith(set);

        return result;
    }

    private static readonly TimeSpan StaleGracePeriod = TimeSpan.FromDays(7);

    private async Task<int> RemoveStaleTopicsAsync(
        IReadOnlySet<string> allKnownNames, IReadOnlySet<string> currentNames, PerformContext? console, CancellationToken ct)
    {
        // Safety: if no provider returned any names, don't remove anything
        if (allKnownNames.Count == 0)
        {
            _logger.LogWarning("No known topic names available — skipping stale topic removal");
            return 0;
        }

        // Load tracked entities only for names that look stale (avoids loading the entire table)
        var originalNameMap = await _queryRepo.GetOriginalNameMappingsAsync(ct);
        var potentiallyStaleNames = currentNames
            .Where(n => !allKnownNames.Contains(n)
                && (!originalNameMap.TryGetValue(n, out var orig) || !allKnownNames.Contains(orig)))
            .ToList();
        if (potentiallyStaleNames.Count == 0)
            return 0;

        var cutoff = DateTime.UtcNow - StaleGracePeriod;

        var allTopics = await _queryRepo.GetByNamesTrackedAsync(potentiallyStaleNames, ct);

        var stale = allTopics
            .Where(c => !allKnownNames.Contains(c.Name)
                && (c.OriginalName is null || !allKnownNames.Contains(c.OriginalName))
                && (c.LastSourceRefresh == null || c.LastSourceRefresh < cutoff))
            .ToList();

        var skippedGrace = allTopics.Count(c => !allKnownNames.Contains(c.Name)
                && (c.OriginalName is null || !allKnownNames.Contains(c.OriginalName)))
            - stale.Count;

        if (skippedGrace > 0)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "{Count} topics not found on provider indexes but within {Days}-day grace period — keeping",
                    skippedGrace, StaleGracePeriod.Days);

            console?.WriteLine($"{skippedGrace} topics missing from indexes but within grace period — keeping.");
        }

        if (stale.Count == 0)
            return 0;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Removing {Count} stale topics not found on any provider index for >{Days} days: {Names}",
                stale.Count, StaleGracePeriod.Days, string.Join(", ", stale.Select(c => c.Name)));

        console?.WriteLine($"Removing {stale.Count} stale topics (absent >{StaleGracePeriod.Days} days): {string.Join(", ", stale.Select(c => c.Name))}");

        await _writeRepo.RemoveRangeAsync(stale, ct);
        await _writeRepo.SaveChangesAsync(ct);

        return stale.Count;
    }
}
