using Hangfire;
using Hangfire.Server;
using Meducate.Application.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Jobs;

internal sealed class TopicRefreshJob(
    TopicRefreshService refreshService,
    ILogger<TopicRefreshJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public Task ExecuteAsync(IJobCancellationToken jobCancellationToken, PerformContext? context = null) =>
        JobRunner.RunAsync("topic refresh", logger, jobCancellationToken, context,
            ct => refreshService.RefreshAllAsync(context, ct));
}
