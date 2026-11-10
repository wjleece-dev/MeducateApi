using System.ComponentModel.DataAnnotations;

namespace Meducate.Application.DTOs;

internal sealed record CreateOrganisationRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression(@"^[\w\s\-&.,''()]+$", ErrorMessage = "Organisation name contains invalid characters.")]
    public string OrganisationName { get; init; } = string.Empty;
}
