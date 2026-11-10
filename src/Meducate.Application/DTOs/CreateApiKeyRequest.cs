using System.ComponentModel.DataAnnotations;

namespace Meducate.Application.DTOs;

internal sealed record CreateApiKeyRequest
{
    [StringLength(100)]
    [RegularExpression(@"^[\w\s\-&.,''()]*$", ErrorMessage = "Key name contains invalid characters.")]
    public string? Name { get; init; }
}
