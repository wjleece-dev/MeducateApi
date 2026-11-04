using Meducate.Infrastructure.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Meducate.Tests;

public class SemanticKernelBuilderTests
{
    private static IConfiguration Config(params (string, string?)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    [Fact]
    public void EscalationModel_IsRegisteredOnlyWhenConfigured()
    {
        var plain = SemanticKernelBuilder.CreateKernel(Config(("OpenAI:ApiKey", "test")));
        Assert.Null(plain.Services.GetKeyedService<IChatCompletionService>(SemanticKernelLLMProcessor.EscalationServiceId));

        var escalating = SemanticKernelBuilder.CreateKernel(Config(("OpenAI:ApiKey", "test"), ("OpenAI:EscalationModel", "gpt-4.1")));
        Assert.NotNull(escalating.Services.GetKeyedService<IChatCompletionService>(SemanticKernelLLMProcessor.EscalationServiceId));
        Assert.NotNull(escalating.Services.GetService<IChatCompletionService>());
    }
}
