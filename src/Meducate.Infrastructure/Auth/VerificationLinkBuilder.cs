using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;

namespace Meducate.Infrastructure.Auth;

internal sealed class VerificationLinkBuilder(IConfiguration config)
{
    private readonly string _baseUrl =
        (config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl not configured."))
        .TrimEnd('/');

    public string Build(string token)
    {
        var safeToken = Uri.EscapeDataString(token);
        return $"{_baseUrl}/auth/verify?token={safeToken}";
    }
}
