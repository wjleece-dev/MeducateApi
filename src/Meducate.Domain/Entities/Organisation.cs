namespace Meducate.Domain.Entities;

internal sealed class Organisation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public List<ApiClient> ApiClients { get; set; } = [];
}
