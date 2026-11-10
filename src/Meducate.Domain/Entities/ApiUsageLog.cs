namespace Meducate.Domain.Entities;

internal sealed class ApiUsageLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClientId { get; set; }
    public ApiClient Client { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? Endpoint { get; set; }
    public string? Method { get; set; }
    public int StatusCode { get; set; }

    public string? OrganisationName { get; set; }
    public string? QueryString { get; set; }
}
