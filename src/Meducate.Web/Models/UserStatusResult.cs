
namespace Meducate.Web.Models;

internal sealed record UserStatusResult(string Email, bool IsEmailVerified, Guid? OrganisationId, string? OrganisationName, bool HasApiKeys);
