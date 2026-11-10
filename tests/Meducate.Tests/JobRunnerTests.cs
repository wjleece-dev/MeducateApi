using Hangfire;
using Meducate.Application.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meducate.Tests;

public class JobRunnerTests
{
    [Fact]
    public async Task RunAsync_CallsWork_OnSuccess()
    {
        var called = false;

        await JobRunner.RunAsync("test-job", NullLogger.Instance, new FakeJobCancellationToken(), null,
            _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    [Fact]
    public async Task RunAsync_SwallowsOperationCanceledException()
    {
        // No exception should propagate — reaching the end of this test is the assertion.
        await JobRunner.RunAsync("test-job", NullLogger.Instance, new FakeJobCancellationToken(), null,
            _ => throw new OperationCanceledException());
    }

    [Fact]
    public async Task RunAsync_RethrowsOtherExceptions()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JobRunner.RunAsync("test-job", NullLogger.Instance, new FakeJobCancellationToken(), null,
                _ => throw new InvalidOperationException("boom")));
    }

    private sealed class FakeJobCancellationToken : IJobCancellationToken
    {
        public CancellationToken ShutdownToken => CancellationToken.None;
        public void ThrowIfCancellationRequested() { }
    }
}
