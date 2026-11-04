using Hangfire;
using Meducate.API.Endpoints;
using Meducate.API.Middleware;
using Meducate.Application.Jobs;
using Microsoft.AspNetCore.HttpOverrides;

namespace Meducate.API.Infrastructure;

internal static class MiddlewarePipeline
{
    private static bool RefreshSucceededRecently()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-20);
            return JobStorage.Current.GetMonitoringApi().SucceededJobs(0, 100)
                .Any(j => j.Value?.Job?.Type == typeof(TopicRefreshJob) && j.Value.SucceededAt > cutoff);
        }
        catch
        {
            // Cannot tell, so keep the old behaviour of running the catch-up.
            return false;
        }
    }

    internal static WebApplication UseMeducatePipeline(this WebApplication app)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        app.UseResponseCompression();
        app.UseRouting();
        app.UseCors();

        // Outermost middleware — wraps everything so all responses get headers and exceptions are caught
        app.UseMiddleware<GlobalExceptionMiddleware>();
        // Body-less error responses (405, unbindable query parameters, unknown routes) become problem+json like the rest
        app.UseStatusCodePages();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<ETagMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestTimingMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseMiddleware<UsageLoggingMiddleware>();

        app.UseSwagger();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI();
        }

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new HangfireDashboardAuthFilter()]
        });

        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();

        jobManager.RemoveIfExists("upsert-medical-conditions");

        jobManager.AddOrUpdate<TopicDiscoveryJob>(
            "discover-medical-conditions",
            job => job.ExecuteAsync(JobCancellationToken.Null),
            "0 2 * * *"); // 2 AM UTC

        jobManager.AddOrUpdate<TopicRefreshJob>(
            "refresh-medical-conditions",
            job => job.ExecuteAsync(JobCancellationToken.Null),
            "0 3 * * *"); // 3 AM UTC

        jobManager.AddOrUpdate<DataIntegrityCheckJob>(
            "data-integrity-check",
            job => job.ExecuteAsync(JobCancellationToken.Null),
            "0 4 * * *"); // 4 AM UTC — runs after refresh completes

        // Catch-up run for a missed 3 AM refresh. Only when none has succeeded recently: every deploy restarts the
        // container, and re-running the refresh each time re-processed (and re-paid for) the same flagged backlog.
        if (!app.Environment.IsDevelopment() && !RefreshSucceededRecently())
        {
            var jobClient = app.Services.GetRequiredService<IBackgroundJobClient>();
            jobClient.Enqueue<TopicRefreshJob>(job => job.ExecuteAsync(JobCancellationToken.Null));
        }

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        });
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });
        app.MapGet("/", () => Results.Ok(new
        {
            name = "Meducate API",
            version = "v1",
            docs = "https://meducateapi.com/docs",
            openapi = "/swagger/v1/swagger.json",
            health = "/health",
        })).ExcludeFromDescription();
        app.MapPost(HangfireDashboardAuthFilter.LoginPath, (HttpContext http, IConfiguration config) => HangfireDashboardAuthFilter.Login(http, config))
            .ExcludeFromDescription();
        var v1 = app.MapGroup("/api/v1");
        v1.MapAuthEndpoints();
        v1.MapOrgEndpoints();
        v1.MapTopicEndpoints();
        v1.MapWaitlistEndpoints();
        app.MapInternalEndpoints();

        return app;
    }
}
