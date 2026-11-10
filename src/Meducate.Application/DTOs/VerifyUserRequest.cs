using System.ComponentModel.DataAnnotations;

namespace Meducate.Application.DTOs;

internal sealed record VerifyUserRequest
{
    [Required, StringLength(128)]
    public string Token { get; init; } = string.Empty;
}
