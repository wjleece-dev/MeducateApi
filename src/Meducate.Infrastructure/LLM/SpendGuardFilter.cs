using Meducate.Domain.Services;
using Microsoft.SemanticKernel;

namespace Meducate.Infrastructure.LLM;

// Wraps every kernel function call: refuses it once the daily budget is spent, and records the tokens used.
internal sealed class SpendGuardFilter(ILlmSpendTracker tracker, string defaultModel) : IFunctionInvocationFilter
{
    private const int EstimatedPromptTokens = 1_500;

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        await tracker.EnsureWithinBudgetAsync(context.CancellationToken);

        await next(context);

        if (context.Result.Metadata?.GetValueOrDefault("Usage") is OpenAI.Chat.ChatTokenUsage usage)
        {
            string? model = null;
            try
            {
                model = context.Result.GetValue<ChatMessageContent>()?.ModelId;
            }
            catch (InvalidCastException)
            {
                // Not a chat message: fall back to the configured model for pricing.
            }

            await tracker.RecordAsync(string.IsNullOrWhiteSpace(model) ? defaultModel : model, usage.InputTokenCount, usage.OutputTokenCount, context.CancellationToken);
        }
        else
        {
            // No usage metadata came back. Estimate from the text (about three characters a token, plus the prompt
            // template) rather than let the call go unrecorded, so the budget still bites.
            var inputChars = context.Arguments.Sum(kv => kv.Value is string s ? s.Length : 0);
            var outputChars = context.Result.GetValue<object>()?.ToString()?.Length ?? 0;
            await tracker.RecordAsync(defaultModel, inputChars / 3 + EstimatedPromptTokens, outputChars / 3, context.CancellationToken);
        }
    }
}
