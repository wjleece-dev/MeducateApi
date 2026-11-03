using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meducate.API;
using Microsoft.AspNetCore.WebUtilities;

namespace Meducate.IntegrationTests;

public class ApiEndToEndTests(MeducateApiFactory factory) : IClassFixture<MeducateApiFactory>
{
    private static HttpClient NewClient(MeducateApiFactory factory) => factory.CreateClient();

    private static HttpClient NewClientWithCsrfHeader(MeducateApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-By", "MeducateAPI");
        return client;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await NewClient(factory).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnversionedTopicsRoute_NoLongerExists()
    {
        // Regression check for the /api -> /api/v1 migration.
        var response = await NewClient(factory).GetAsync("/api/topics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VersionedTopicsRoute_WithDemoKey_ReturnsOkWithRateLimitHeaders()
    {
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiConstants.DemoRawKey);

        var response = await client.GetAsync("/api/v1/topics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
    }

    [Fact]
    public async Task MutatingRequest_WithoutCsrfHeaderOrApiKey_IsForbidden()
    {
        var response = await NewClient(factory).PostAsJsonAsync("/api/v1/waitlist", new { email = "csrf-check@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterVerifyAndFetchSession_FullFlow()
    {
        var client = NewClientWithCsrfHeader(factory);
        var email = $"integration-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/users/register", new { email, acceptedTerms = true });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var verifyUrl = factory.EmailService.LastVerificationUrl;
        Assert.NotNull(verifyUrl);
        var token = QueryHelpers.ParseQuery(new Uri(verifyUrl!).Query)["token"].ToString();
        Assert.NotEmpty(token);

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/users/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var authCookie = ExtractSetCookieValue(verifyResponse, "meducateapi_auth");
        Assert.NotNull(authCookie);

        var sessionClient = NewClientWithCsrfHeader(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", authCookie);

        var meResponse = await sessionClient.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        Assert.Equal(email, meDoc.RootElement.GetProperty("email").GetString());
        Assert.True(meDoc.RootElement.GetProperty("isEmailVerified").GetBoolean());
    }

    [Fact]
    public async Task CreateOrganisationAndApiKey_FullFlow()
    {
        var client = NewClientWithCsrfHeader(factory);
        var email = $"integration-org-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/users/register", new { email, acceptedTerms = true });
        var token = QueryHelpers.ParseQuery(new Uri(factory.EmailService.LastVerificationUrl!).Query)["token"].ToString();
        var verifyResponse = await client.PostAsJsonAsync("/api/v1/users/verify", new { token });
        var authCookie = ExtractSetCookieValue(verifyResponse, "meducateapi_auth")!;

        var sessionClient = NewClientWithCsrfHeader(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", authCookie);

        var orgResponse = await sessionClient.PostAsJsonAsync("/api/v1/orgs", new { organisationName = "Integration Test Org" });
        Assert.Equal(HttpStatusCode.Created, orgResponse.StatusCode);
        var orgId = (await orgResponse.Content.ReadAsStringAsync()).Trim('"');

        var keyResponse = await sessionClient.PostAsJsonAsync($"/api/v1/orgs/{orgId}/keys", new { name = "Test Key" });
        Assert.Equal(HttpStatusCode.Created, keyResponse.StatusCode);

        using var keyDoc = JsonDocument.Parse(await keyResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(keyDoc.RootElement.GetProperty("apiKey").GetString()));
    }

    // --- Waitlist -----------------------------------------------------------------------------
    // Each test uses its own X-Forwarded-For address: the waitlist rate limit (5/hour) is
    // per client address, and these tests would otherwise share one partition and trip it.

    private HttpClient WaitlistClient(string clientAddress)
    {
        var client = NewClientWithCsrfHeader(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
        return client;
    }

    private static string FormRenderedAgo(TimeSpan age) =>
        DateTimeOffset.UtcNow.Add(-age).ToUnixTimeMilliseconds().ToString();

    private static object WaitlistBody(string email, string? website = null, string? timestamp = null) =>
        new { email, website, timestamp = timestamp ?? FormRenderedAgo(TimeSpan.FromSeconds(30)) };

    private async Task<List<string>> StoredWaitlistEmailsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/waitlist?take=200");
        request.Headers.Add("X-Internal-Token", MeducateApiFactory.InternalToken);

        var response = await NewClient(factory).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("email").GetString()!)
            .ToList();
    }

    [Fact]
    public async Task Waitlist_StoresSignup_AndDoesNotListTheSameAddressTwice()
    {
        var client = WaitlistClient("10.20.0.1");
        var email = $"wl-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/v1/waitlist", WaitlistBody(email));
        var again = await client.PostAsJsonAsync("/api/v1/waitlist", WaitlistBody(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, (await StoredWaitlistEmailsAsync()).Count(e => e == email));
    }

    [Fact]
    public async Task Waitlist_FilledHoneypot_LooksSuccessfulButStoresNothing()
    {
        var email = $"wl-bot-{Guid.NewGuid():N}@example.com";

        var response = await WaitlistClient("10.20.0.2").PostAsJsonAsync("/api/v1/waitlist", WaitlistBody(email, website: "http://spam.example"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(email, await StoredWaitlistEmailsAsync());
    }

    [Fact]
    public async Task Waitlist_SubmittedImpossiblyFast_LooksSuccessfulButStoresNothing()
    {
        var email = $"wl-fast-{Guid.NewGuid():N}@example.com";

        var response = await WaitlistClient("10.20.0.3").PostAsJsonAsync("/api/v1/waitlist",
            WaitlistBody(email, timestamp: FormRenderedAgo(TimeSpan.FromMilliseconds(200))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(email, await StoredWaitlistEmailsAsync());
    }

    [Fact]
    public async Task Waitlist_WithoutRenderTimestamp_IsRejected()
    {
        var response = await WaitlistClient("10.20.0.4").PostAsJsonAsync("/api/v1/waitlist",
            new { email = $"wl-nots-{Guid.NewGuid():N}@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two words@example.com")]
    public async Task Waitlist_InvalidEmail_IsRejected(string email)
    {
        var response = await WaitlistClient("10.20.0.5").PostAsJsonAsync("/api/v1/waitlist", WaitlistBody(email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Waitlist_SixthSubmissionInAnHourFromOneAddress_IsRateLimited()
    {
        var client = WaitlistClient("10.20.0.99");

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/v1/waitlist", WaitlistBody($"wl-rl{i}-{Guid.NewGuid():N}@example.com"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/v1/waitlist", WaitlistBody($"wl-rl6-{Guid.NewGuid():N}@example.com"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task InternalWaitlist_WithoutToken_IsUnauthorized()
    {
        var response = await NewClient(factory).GetAsync("/internal/waitlist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string? ExtractSetCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase))
                return cookie[..cookie.IndexOf(';')];
        }

        return null;
    }
}
