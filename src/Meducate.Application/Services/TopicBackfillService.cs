using Hangfire.Console;
using Hangfire.Server;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Services;

internal sealed class TopicBackfillService(
    ITopicQueryRepository queryRepo,
    ITopicWriteRepository writeRepo,
    ILLMProcessor llmProcessor,
    IIcd11CodingService icd11Service,
    ILogger<TopicBackfillService> logger)
{
    private readonly ITopicQueryRepository _queryRepo = queryRepo;
    private readonly ITopicWriteRepository _writeRepo = writeRepo;
    private readonly ILLMProcessor _llmProcessor = llmProcessor;
    private readonly IIcd11CodingService _icd11Service = icd11Service;
    private readonly ILogger<TopicBackfillService> _logger = logger;

    internal async Task<int> BackfillTopicTypesAsync(CancellationToken ct, PerformContext? console = null)
    {
        var unclassified = await _queryRepo.GetUnclassifiedTopicsAsync(ct);

        if (unclassified.Count == 0)
            return 0;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Backfilling TopicType for {Count} unclassified/Other topics", unclassified.Count);

        console?.WriteLine($"Backfilling TopicType for {unclassified.Count} unclassified/Other topics...");

        try
        {
            var topics = unclassified
                .Select(c => new TopicClassifyInput(c.Name, c.Summary))
                .ToList();
            var typeMap = await _llmProcessor.ClassifyTopicNamesAsync(topics, ct);

            var nonMedical = new List<HealthTopic>();

            foreach (var topic in unclassified)
            {
                var newType = typeMap.GetValueOrDefault(topic.Name, TopicConstants.TopicTypeOther);

                if (string.Equals(newType, TopicConstants.TopicTypeNonMedical, StringComparison.OrdinalIgnoreCase))
                {
                    nonMedical.Add(topic);
                    continue;
                }

                topic.TopicType = newType;
            }

            if (nonMedical.Count > 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var nonMedicalNames = string.Join(", ", nonMedical.Select(t => t.Name));
                    _logger.LogInformation("Removing {Count} non-medical topics: {Names}",
                        nonMedical.Count, nonMedicalNames);
                }
                console?.WriteLine($"Removing {nonMedical.Count} non-medical topics: {string.Join(", ", nonMedical.Select(t => t.Name))}");
                await _writeRepo.RemoveRangeAsync(nonMedical, ct);
            }

            await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Backfilled {Reclassified} topics, removed {Removed} non-medical",
                    unclassified.Count - nonMedical.Count, nonMedical.Count);

            console?.WriteLine($"Backfilled {unclassified.Count - nonMedical.Count} topics, removed {nonMedical.Count} non-medical.");

            return unclassified.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill TopicType — will retry next run");
            console?.WriteLine($"Backfill failed: {ex.Message}");

            // Revert tracked changes
            _writeRepo.RevertChanges(unclassified);

            return 0;
        }
    }

    internal async Task<int> BackfillOriginalNamesAsync(IReadOnlySet<string> allKnownNames, CancellationToken ct, PerformContext? console = null)
    {
        var topics = await _queryRepo.GetTopicsWithoutOriginalNameAsync(ct);

        if (topics.Count == 0)
            return 0;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Backfilling OriginalName for {Count} topics", topics.Count);

        console?.WriteLine($"Backfilling OriginalName for {topics.Count} topics...");

        try
        {
            var trivialCount = 0;
            var llmCount = 0;
            var remaining = new List<HealthTopic>();

            // Trivial matches: topics whose Name IS in allKnownNames
            foreach (var topic in topics)
            {
                if (allKnownNames.Contains(topic.Name))
                {
                    topic.OriginalName = topic.Name;
                    trivialCount++;
                }
                else
                {
                    remaining.Add(topic);
                }
            }

            if (remaining.Count > 0)
            {
                // Build unmatched provider names = allKnownNames minus all topics' Name and OriginalName values
                var allTopicNames = await _queryRepo.GetAllTopicNamesAsync(ct);
                var originalNameMap = await _queryRepo.GetOriginalNameMappingsAsync(ct);

                var usedNames = new HashSet<string>(allTopicNames, StringComparer.OrdinalIgnoreCase);
                foreach (var orig in originalNameMap.Values)
                    usedNames.Add(orig);

                var unmatchedProviderNames = allKnownNames.Where(n => !usedNames.Contains(n)).ToArray();

                if (unmatchedProviderNames.Length > 0)
                {
                    var normalizedNames = remaining.Select(t => t.Name).ToList();
                    var matches = await _llmProcessor.MatchOriginalNamesAsync(normalizedNames, unmatchedProviderNames, ct);

                    foreach (var topic in remaining)
                    {
                        if (matches.TryGetValue(topic.Name, out var originalName))
                        {
                            topic.OriginalName = originalName;
                            llmCount++;
                        }
                    }
                }
            }

            if (trivialCount > 0 || llmCount > 0)
                await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Backfilled OriginalName: {Trivial} trivial, {LLM} LLM-matched", trivialCount, llmCount);

            console?.WriteLine($"Backfilled OriginalName: {trivialCount} trivial, {llmCount} LLM-matched.");

            return trivialCount + llmCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill OriginalName — will retry next run");
            console?.WriteLine($"OriginalName backfill failed: {ex.Message}");

            _writeRepo.RevertChanges(topics);

            return 0;
        }
    }

    internal async Task<int> BackfillEmptyStructuredFieldsAsync(CancellationToken ct, PerformContext? console = null)
    {
        var topics = await _queryRepo.GetTopicsWithEmptyStructuredFieldsAsync(ct);

        if (topics.Count == 0)
            return 0;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Flagging {Count} topics with empty structured fields for reprocessing", topics.Count);

        console?.WriteLine($"Flagging {topics.Count} topics with empty structured fields for reprocessing...");

        try
        {
            foreach (var topic in topics)
            {
                topic.NeedsLlmReprocessing = true;
                // Touch LastSourceRefresh so GetTopicsNeedingReprocessingAsync (2-day cutoff) picks these up
                topic.LastSourceRefresh = DateTime.UtcNow;
            }

            await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Flagged {Count} empty-field topics for reprocessing", topics.Count);

            console?.WriteLine($"Flagged {topics.Count} topics for reprocessing.");

            return topics.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flag empty-field topics — will retry next run");
            console?.WriteLine($"Empty-field backfill failed: {ex.Message}");

            _writeRepo.RevertChanges(topics);

            return 0;
        }
    }

    // Topic names with known-wrong categories and their correct assignments.
    // Only corrects if the current category does NOT match the expected one,
    // so this is idempotent and won't re-trigger on subsequent runs.
    private static readonly Dictionary<string, string> CategoryCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Rare Disease", "Symptoms & Signs" },
        { "Rare Diseases", "Symptoms & Signs" },
        { "Drowning", "Injury & Poisoning" },
        { "Choking", "Injury & Poisoning" },
        { "Burns", "Injury & Poisoning" },
        { "Frostbite", "Injury & Poisoning" },
        { "Chronic Illness", "Health & Wellness" },
        { "VLDL Cholesterol", "Endocrine, Nutritional & Metabolic" },
        { "Sleep Apnea", "Sleep-Wake Disorders" },
        { "Insomnia", "Sleep-Wake Disorders" },
        { "Sleep Disorders", "Sleep-Wake Disorders" },
        { "Sleep Deprivation", "Sleep-Wake Disorders" },
        { "Sexual Dysfunction", "Sexual Health" },
        { "Erectile Dysfunction", "Sexual Health" },
        { "Female Sexual Dysfunction", "Sexual Health" },
        { "Sarcoidosis", "Blood & Immune System" },
        { "Heat Illness", "Symptoms & Signs" },
        { "Hay Fever", "Respiratory System" },
        { "Meningitis", "Infectious & Parasitic Diseases" },
    };

    // Topic names that were incorrectly renamed by the synonym-merge logic (LLM decided a
    // differently-scoped candidate was "the same broader subject" and renamed this entry to
    // match it) and their correct name, restored from OriginalName once the mistake is found.
    // Only corrects if the current name still matches the wrong value, so this is idempotent.
    // Order matters when a correction's target name is itself occupied by another wrong name
    // being corrected in the same pass — earlier entries free up names
    // that later entries need, and each correction is saved individually so later ones see it.
    // Currently empty: the COVID-19 disease/vaccine/testing mix-up it was added for is fixed
    // (three separate topics, each correctly typed). Entries left in place after a fix only log
    // "target name already in use" warnings every night and are a latent hazard -- e.g. if the
    // COVID-19 Testing topic were ever removed, a stale rule would rename the real COVID-19
    // disease topic to it. Add an entry when a merge goes wrong, remove it once it has landed.
    private static readonly List<(string WrongName, string CorrectName)> NameCorrections = [];

    internal async Task<int> BackfillBadNamesAsync(CancellationToken ct, PerformContext? console = null)
    {
        var correctedCount = 0;

        foreach (var (wrongName, correctName) in NameCorrections)
        {
            var topic = await _queryRepo.GetByNameTrackedAsync(wrongName, ct);
            if (topic is null)
                continue;

            // Renaming to a name another topic already holds would violate the name
            // uniqueness constraint and take the whole SaveChanges (and everything after
            // it in this job) down with it. Skip for now — it'll retry next run once
            // whatever holds that name has moved (or stop trying if it's a real conflict).
            var collision = await _queryRepo.GetByNameTrackedAsync(correctName, ct);
            if (collision is not null)
            {
                _logger.LogWarning("Skipping name correction '{Old}' → '{New}': target name already in use", wrongName, correctName);
                console?.WriteLine($"  [{wrongName}] skipped rename to '{correctName}' — name already taken");
                continue;
            }

            topic.Name = correctName;

            try
            {
                await _writeRepo.SaveChangesAsync(ct);

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Corrected name for topic: '{Old}' → '{New}'", wrongName, correctName);

                console?.WriteLine($"  [{wrongName}] renamed → {correctName}");
                correctedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save name correction '{Old}' → '{New}'", wrongName, correctName);
                console?.WriteLine($"  [{wrongName}] rename to '{correctName}' failed: {ex.Message}");
            }
        }

        return correctedCount;
    }

    internal async Task<int> BackfillBadCategoriesAsync(CancellationToken ct, PerformContext? console = null)
    {
        var corrected = new List<HealthTopic>();

        foreach (var (name, correctCategory) in CategoryCorrections)
        {
            var topic = await _queryRepo.GetByNameTrackedAsync(name, ct);
            if (topic is null)
                continue;

            // Skip if already correct (or already null — will be picked up by BackfillCategoriesAsync)
            if (topic.Category is null || string.Equals(topic.Category, correctCategory, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Correcting category for '{Name}': '{Old}' → '{New}'",
                    topic.Name, topic.Category, correctCategory);

            console?.WriteLine($"  [{topic.Name}] {topic.Category} → {correctCategory}");

            topic.Category = correctCategory;
            corrected.Add(topic);
        }

        if (corrected.Count == 0)
            return 0;

        try
        {
            await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Corrected categories on {Count} topics", corrected.Count);

            console?.WriteLine($"Corrected {corrected.Count} miscategorized topics.");

            return corrected.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to correct bad categories — will retry next run");
            console?.WriteLine($"Bad category correction failed: {ex.Message}");

            _writeRepo.RevertChanges(corrected);

            return 0;
        }
    }

    // True when the last BackfillCategoriesAsync call could not reach the classifier; callers must not treat
    // the topics it left uncategorised as unclassifiable.
    internal bool CategoryBackfillFailed { get; private set; }

    internal async Task<int> BackfillCategoriesAsync(CancellationToken ct, PerformContext? console = null)
    {
        CategoryBackfillFailed = false;
        var uncategorized = await _queryRepo.GetUncategorizedTopicsAsync(ct);

        var fixedCount = 0;
        foreach (var topic in uncategorized.Where(t => t.TopicType is not null && TopicConstants.CategoryByType.ContainsKey(t.TopicType)).ToList())
        {
            topic.Category = TopicConstants.CategoryByType[topic.TopicType!];
            uncategorized.Remove(topic);
            fixedCount++;
        }

        if (fixedCount > 0)
            await _writeRepo.SaveChangesAsync(ct);

        if (uncategorized.Count == 0)
            return fixedCount;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Backfilling categories for {Count} uncategorized topics", uncategorized.Count);

        console?.WriteLine($"Backfilling categories for {uncategorized.Count} uncategorized topics...");

        try
        {
            var inputs = uncategorized
                .Select(c => new TopicCategoryInput(c.Name, c.TopicType ?? TopicConstants.TopicTypeOther, c.Summary))
                .ToList();
            var categoryMap = await _llmProcessor.ClassifyTopicCategoriesAsync(inputs, ct);

            var assigned = 0;
            foreach (var topic in uncategorized)
            {
                if (categoryMap.TryGetValue(topic.Name, out var category))
                {
                    topic.Category = category;
                    assigned++;
                }
            }

            await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Assigned categories to {Count} topics", assigned);

            console?.WriteLine($"Assigned categories to {assigned} topics.");

            return assigned + fixedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill categories — will retry next run");
            console?.WriteLine($"Category backfill failed: {ex.Message}");

            _writeRepo.RevertChanges(uncategorized);
            CategoryBackfillFailed = true;

            return fixedCount;
        }
    }

    internal async Task<int> BackfillIcd11CodesAsync(CancellationToken ct, PerformContext? console = null)
    {
        var topics = await _queryRepo.GetTopicsNeedingIcd11Async(TopicConstants.Icd11CodeableTypes, ct);

        if (topics.Count == 0)
            return 0;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Looking up ICD-11 codes for {Count} topics", topics.Count);

        console?.WriteLine($"Looking up ICD-11 codes for {topics.Count} topics...");

        var matched = 0;
        foreach (var topic in topics)
        {
            ct.ThrowIfCancellationRequested();

            var match = await _icd11Service.LookupAsync(topic.Name, ct);
            if (match is null)
                continue;

            topic.Icd11Code = match.Code;
            topic.Icd11Title = match.Title;
            matched++;
        }

        if (matched == 0)
            return 0;

        try
        {
            await _writeRepo.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Assigned ICD-11 codes to {Count} topics", matched);

            console?.WriteLine($"Assigned ICD-11 codes to {matched} topics.");

            return matched;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save ICD-11 codes — will retry next run");
            console?.WriteLine($"ICD-11 backfill save failed: {ex.Message}");

            _writeRepo.RevertChanges(topics);

            return 0;
        }
    }
}
