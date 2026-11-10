namespace Meducate.Domain.Entities;

internal sealed class SeenTopic
{
    public string Name { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? TopicType { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
}
