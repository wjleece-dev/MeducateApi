namespace Meducate.Domain.Services;

internal sealed record Icd11Match(string Code, string Title);

internal interface IIcd11CodingService
{
    Task<Icd11Match?> LookupAsync(string topicName, CancellationToken ct = default);
}
