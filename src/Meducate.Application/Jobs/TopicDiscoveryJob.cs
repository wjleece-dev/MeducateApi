using Hangfire;
using Hangfire.Server;
using Meducate.Application.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Jobs;

internal sealed class TopicDiscoveryJob(
    TopicIngestionService ingestionService,
    ILogger<TopicDiscoveryJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public Task ExecuteAsync(IJobCancellationToken jobCancellationToken, PerformContext? context = null) =>
        JobRunner.RunAsync("topic discovery", logger, jobCancellationToken, context,
            ct => ingestionService.IngestAsync(context, ct));
}
