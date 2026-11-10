using System.Security.Cryptography;
using System.Text;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal static class ContentHasher
{
    internal static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16];
    }

    internal static string GetSourceHash(IEnumerable<RawTopicData> results, string mergedRawSource)
    {
        var providerHash = results.FirstOrDefault(r => r.ContentHash is not null)?.ContentHash;
        return providerHash ?? ComputeHash(mergedRawSource);
    }
}
