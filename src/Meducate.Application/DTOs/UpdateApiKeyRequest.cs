using System.ComponentModel.DataAnnotations;

namespace Meducate.Application.DTOs;

internal sealed record UpdateApiKeyRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression(@"^[\w\s\-&.,''()]+$", ErrorMessage = "Key name contains invalid characters.")]
    public string Name { get; init; } = "";
}
