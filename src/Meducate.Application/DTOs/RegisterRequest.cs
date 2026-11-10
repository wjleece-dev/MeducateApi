using System.ComponentModel.DataAnnotations;

namespace Meducate.Application.DTOs;

internal sealed record RegisterRequest
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    public string? Mode { get; init; }

    public bool AcceptedTerms { get; init; }

    public string? TermsVersion { get; init; }

    public string? Website { get; init; }

    public string? Timestamp { get; init; }
}
