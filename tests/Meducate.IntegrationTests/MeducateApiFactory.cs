using Meducate.Domain.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Meducate.IntegrationTests;

public sealed class MeducateApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public const string InternalToken = "integration-internal-token";

    public FakeEmailService EmailService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        // Real client is never exercised — IEmailService is replaced below — but
        // InfrastructureServiceRegistration throws at startup if this is blank.
        builder.UseSetting("Resend:ApiToken", "test-token");
        // Without this, the app immediately picks up and executes real background
        // jobs against real external APIs the moment the test host boots.
        builder.UseSetting("Hangfire:DisableServer", "true");
        // Lets tests read stored waitlist signups back through /internal/waitlist.
        builder.UseSetting("Internal:TriggerToken", InternalToken);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(EmailService);
        });
    }

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }
}
