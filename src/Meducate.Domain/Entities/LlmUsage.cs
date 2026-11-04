namespace Meducate.Domain.Entities;

// One row per model call, so spend is visible and a daily budget survives restarts and deploys.
internal sealed class LlmUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Model { get; set; } = null!;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}
