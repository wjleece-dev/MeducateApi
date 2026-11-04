using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Meducate.Infrastructure.LLM;

internal static class SemanticKernelBuilder
{
    public static Kernel CreateKernel(IConfiguration config, Meducate.Domain.Services.ILlmSpendTracker? spendTracker = null)
    {
        var openAiKey = config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(openAiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        var model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: model,
            apiKey: openAiKey
        );

        // Optional stronger model for topics that already failed a quality check once; off unless configured.
        var escalationModel = config["OpenAI:EscalationModel"];
        if (!string.IsNullOrWhiteSpace(escalationModel))
            builder.AddOpenAIChatCompletion(modelId: escalationModel, apiKey: openAiKey, serviceId: SemanticKernelLLMProcessor.EscalationServiceId);

        var kernel = builder.Build();

        // Every call goes through the budget check and the usage ledger.
        if (spendTracker is not null)
            kernel.FunctionInvocationFilters.Add(new SpendGuardFilter(spendTracker, model));

        return kernel;
    }
}
