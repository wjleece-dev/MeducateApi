namespace Meducate.Web.Models;

internal sealed class ApiKeyInfo
{
    public Guid Id { get; set; }
    public string KeyId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int DailyLimit { get; set; }
    public int UsageToday { get; set; }
}
