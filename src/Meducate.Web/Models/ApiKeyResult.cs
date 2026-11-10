
namespace Meducate.Web.Models;

internal sealed class ApiKeyResult
{
    public Guid Id { get; set; }
    public string KeyId { get; set; } = "";
    public Guid OrganisationId { get; set; }
    public string ApiKey { get; set; } = "";
}