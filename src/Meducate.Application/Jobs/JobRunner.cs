using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Jobs;

internal static class JobRunner
{
    // Shared boundary for fire-and-forget Hangfire jobs: start/finish logging,
    // graceful-shutdown handling, and failure logging + rethrow for retries.
    public static async Task RunAsync(
        string name,
        ILogger logger,
        IJobCancellationToken token,
        PerformContext? context,
        Func<CancellationToken, Task> work)
    {
        try
        {
            logger.LogInformation("Starting {Job}", name);
            context?.WriteLine($"Starting {name}...");
            await work(token.ShutdownToken);
            logger.LogInformation("{Job} completed", name);
            context?.WriteLine($"{name} completed.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Job} was cancelled (shutdown)", name);
            context?.WriteLine("Job cancelled (shutdown).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Job} failed", name);
            context?.WriteLine($"Job failed: {ex.Message}");
            throw;
        }
    }
}
