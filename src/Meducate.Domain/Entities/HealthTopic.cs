using System.Text.Json.Serialization;

namespace Meducate.Domain.Entities;

internal sealed class HealthTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = null!;

    [JsonIgnore]
    public string? OriginalName { get; set; }

    public string? Summary { get; set; }

    public List<string>? Observations { get; set; }

    public List<string>? Factors { get; set; }

    public List<string>? Actions { get; set; }

    public List<string>? Citations { get; set; }

    [JsonIgnore]
    public string? RawSource { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public string? Category { get; set; }

    public string? TopicType { get; set; }

    public string? Icd11Code { get; set; }

    public string? Icd11Title { get; set; }

    public List<string>? Tags { get; set; }

    public int Version { get; set; } = 1;

    [JsonIgnore]
    public string? SourceHash { get; set; }

    [JsonIgnore]
    public DateTime? LastSourceRefresh { get; set; }

    [JsonIgnore]
    public bool NeedsLlmReprocessing { get; set; }

    [JsonIgnore]
    public int ReprocessAttempts { get; set; }
}
