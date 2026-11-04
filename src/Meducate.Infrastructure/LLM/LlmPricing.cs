namespace Meducate.Infrastructure.LLM;

// USD per million tokens. A model that is not listed is priced as the most expensive one, so an
// unexpected model can only make the budget trip early, never late.
internal static class LlmPricing
{
    // Longest prefixes first: a dated name such as gpt-4o-mini-2024-07-18 must not match plain gpt-4o.
    private static readonly (string Prefix, decimal Input, decimal Output)[] Prices =
    [
        ("gpt-4.1-nano", 0.10m, 0.40m),
        ("gpt-4.1-mini", 0.40m, 1.60m),
        ("gpt-4.1", 2.00m, 8.00m),
        ("gpt-4o-mini", 0.15m, 0.60m),
        ("gpt-4o", 2.50m, 10.00m),
    ];

    private const decimal UnknownInput = 2.50m;
    private const decimal UnknownOutput = 10.00m;

    internal static decimal CostUsd(string? model, long inputTokens, long outputTokens)
    {
        var name = (model ?? string.Empty).Trim().ToLowerInvariant();
        var (input, output) = (UnknownInput, UnknownOutput);

        foreach (var price in Prices)
        {
            if (name.StartsWith(price.Prefix, StringComparison.Ordinal))
            {
                (input, output) = (price.Input, price.Output);
                break;
            }
        }

        return (inputTokens * input + outputTokens * output) / 1_000_000m;
    }
}
