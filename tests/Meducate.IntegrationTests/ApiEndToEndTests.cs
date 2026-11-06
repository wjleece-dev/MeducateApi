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
    public async Task SignIn_WithUnknownEmail_GivesTheSameAnswerAsAKnownOne()
    {
        var client = NewClientWithCsrfHeader(factory);

        var response = await client.PostAsJsonAsync("/api/v1/users/register",
            new { email = "nobody-registered-here@example.com", mode = "signin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("If an account exists", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HangfireDashboard_RequiresTheLoginAndAcceptsTheSessionCookieItIssues()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var anonymous = await client.GetAsync("/hangfire");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("action=\"/hangfire-login\"", await anonymous.Content.ReadAsStringAsync());

        var wrong = await client.PostAsync("/hangfire-login", new FormUrlEncodedContent([new("password", "nope")]));
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Equal("/hangfire?error=1", wrong.Headers.Location?.OriginalString);
        Assert.False(wrong.Headers.Contains("Set-Cookie"));

        var right = await client.PostAsync("/hangfire-login", new FormUrlEncodedContent([new("password", MeducateApiFactory.DashboardPassword)]));
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Equal("/hangfire", right.Headers.Location?.OriginalString);
        var cookie = Assert.Single(right.Headers.GetValues("Set-Cookie"), c => c.StartsWith("HangfireSession=")).Split(';')[0];
        Assert.DoesNotContain(MeducateApiFactory.DashboardPassword, cookie);

        var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        request.Headers.Add("Cookie", cookie);
        var dashboard = await client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Unauthorized, dashboard.StatusCode);
    }

    [Fact]
    public async Task Root_ReturnsServiceInfoWithDocsLink()
    {
        var response = await NewClient(factory).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("https://meducateapi.com/docs", doc.RootElement.GetProperty("docs").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/v1/topics?take=abc", HttpStatusCode.BadRequest)]
    [InlineData("POST", "/api/v1/topics", HttpStatusCode.MethodNotAllowed)]
    public async Task BodylessErrors_AreReturnedAsProblemJson(string method, string path, HttpStatusCode expected)
    {
        var client = NewClientWithCsrfHeader(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiConstants.DemoRawKey);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownTopic_Returns404WithGuidanceAndSuggestions()
    {
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiConstants.DemoRawKey);

        var response = await client.GetAsync("/api/v1/topics/zzz-not-a-topic");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("/topics/search", doc.RootElement.GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("suggestions").ValueKind);
    }

    [Theory]
    [InlineData("/api/v1/topics/No%2FSuch%20Topic")]
    [InlineData("/api/v1/topics/No/Such%20Topic")]
    public async Task TopicNamesWithASlash_ReachTheTopicLookup(string path)
    {
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiConstants.DemoRawKey);

        var response = await client.GetAsync(path);

        // A routing miss would be a bare 404; reaching the handler gives the guidance body.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("/topics/search", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Swagger_ExposesOnlyThePublicSurface()
    {
        var json = await NewClient(factory).GetStringAsync("/swagger/v1/swagger.json");
        using var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(doc.RootElement.GetProperty("paths").EnumerateObject(), p => p.Name.StartsWith("/internal"));
        Assert.Equal("https://api.meducateapi.com", doc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());

        var topics = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/topics").GetProperty("get").GetProperty("responses");
        Assert.True(topics.TryGetProperty("401", out _) && topics.TryGetProperty("429", out _));
    }

    [Fact]
    public async Task Swagger_HealthTopicSchema_UsesWireFieldNames()
    {
        var json = await NewClient(factory).GetStringAsync("/swagger/v1/swagger.json");
        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("HealthTopic").GetProperty("properties");

        foreach (var wire in new[] { "symptoms", "causes", "treatments", "name", "icd11Code" })
            Assert.True(props.TryGetProperty(wire, out _), $"missing {wire}");
        foreach (var internalName in new[] { "observations", "factors", "actions" })
            Assert.False(props.TryGetProperty(internalName, out _), $"unexpected {internalName}");
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

    // Everything the signed-in dashboard does, end to end against the real database: sign in again with a
    // magic link, create and use a key, read usage, rename, revoke, sign out, delete the account.
    [Fact]
    public async Task SignedInLifecycle_KeyUseUsageRenameRevokeSignOutAndAccountDeletion()
    {
        var email = $"lifecycle-{Guid.NewGuid():N}@example.com";

        async Task<HttpClient> SignInAsync(string mode, string address)
        {
            var anonymous = NewClientWithCsrfHeader(factory);
            var register = await anonymous.PostAsJsonAsync("/api/v1/users/register", new { email = address, acceptedTerms = true, mode });
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            var token = QueryHelpers.ParseQuery(new Uri(factory.EmailService.LastVerificationUrl!).Query)["token"].ToString();
            var verify = await anonymous.PostAsJsonAsync("/api/v1/users/verify", new { token });
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

            var session = NewClientWithCsrfHeader(factory);
            session.DefaultRequestHeaders.Add("Cookie", ExtractSetCookieValue(verify, "meducateapi_auth")!);
            return session;
        }

        var session = await SignInAsync("create", email);
        var orgId = (await (await session.PostAsJsonAsync("/api/v1/orgs", new { organisationName = "Lifecycle Org" })).Content.ReadAsStringAsync()).Trim('"');

        using var created = JsonDocument.Parse(await (await session.PostAsJsonAsync($"/api/v1/orgs/{orgId}/keys", new { name = "First key" })).Content.ReadAsStringAsync());
        var rawKey = created.RootElement.GetProperty("apiKey").GetString()!;

        // The key works, and responses carry its limits.
        var keyClient = NewClient(factory);
        keyClient.DefaultRequestHeaders.Add("X-Api-Key", rawKey);
        var use = await keyClient.GetAsync("/api/v1/topics/types");
        Assert.Equal(HttpStatusCode.OK, use.StatusCode);
        Assert.Equal("1000", use.Headers.GetValues("X-RateLimit-Limit").Single());

        // It shows up in the key list, with the new name after a rename.
        using var list = JsonDocument.Parse(await session.GetStringAsync($"/api/v1/orgs/{orgId}/keys"));
        var key = Assert.Single(list.RootElement.EnumerateArray());
        var keyGuid = key.GetProperty("id").GetGuid();
        Assert.Equal("First key", key.GetProperty("name").GetString());

        var rename = await session.PatchAsJsonAsync($"/api/v1/orgs/{orgId}/keys/{keyGuid}", new { name = "Renamed key" });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        using var renamed = JsonDocument.Parse(await session.GetStringAsync($"/api/v1/orgs/{orgId}/keys"));
        Assert.Equal("Renamed key", renamed.RootElement[0].GetProperty("name").GetString());

        // Usage endpoints answer for the owner.
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync($"/api/v1/orgs/{orgId}/usage/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync($"/api/v1/orgs/{orgId}/usage/top-endpoints")).StatusCode);

        // Someone else's session cannot read this organisation.
        var stranger = await SignInAsync("create", $"stranger-{Guid.NewGuid():N}@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await stranger.GetAsync($"/api/v1/orgs/{orgId}/keys")).StatusCode);

        // Revoking the key stops it working immediately.
        var revoke = await session.DeleteAsync($"/api/v1/orgs/{orgId}/keys/{keyGuid}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(31)); // the key lookup is cached for 30 seconds
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.GetAsync("/api/v1/topics/types")).StatusCode);

        // Sign out ends the session; signing in again with a magic link works.
        Assert.True((await session.PostAsync("/api/v1/auth/logout", null)).IsSuccessStatusCode);
        var again = await SignInAsync("signin", email);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/v1/users/me")).StatusCode);

        // Deleting the account removes the data and ends the session.
        Assert.Equal(HttpStatusCode.NoContent, (await again.DeleteAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Backup_ExportContainsNoSecrets_AndRestoreBringsADeletedAccountBack()
    {
        var email = $"backup-{Guid.NewGuid():N}@example.com";
        var anonymous = NewClientWithCsrfHeader(factory);
        await anonymous.PostAsJsonAsync("/api/v1/users/register", new { email, acceptedTerms = true });
        var token = QueryHelpers.ParseQuery(new Uri(factory.EmailService.LastVerificationUrl!).Query)["token"].ToString();
        var verify = await anonymous.PostAsJsonAsync("/api/v1/users/verify", new { token });
        var session = NewClientWithCsrfHeader(factory);
        session.DefaultRequestHeaders.Add("Cookie", ExtractSetCookieValue(verify, "meducateapi_auth")!);
        var orgId = (await (await session.PostAsJsonAsync("/api/v1/orgs", new { organisationName = "Backup Org" })).Content.ReadAsStringAsync()).Trim('"');
        using var key = JsonDocument.Parse(await (await session.PostAsJsonAsync($"/api/v1/orgs/{orgId}/keys", new { name = "Backup key" })).Content.ReadAsStringAsync());
        var rawKey = key.RootElement.GetProperty("apiKey").GetString()!;

        HttpRequestMessage Internal(HttpMethod method, string path, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.Add("X-Internal-Token", MeducateApiFactory.InternalToken);
            return request;
        }

        var export = await NewClient(factory).SendAsync(Internal(HttpMethod.Get, "/internal/backup/export"));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exported = await export.Content.ReadAsStringAsync();
        Assert.Contains(email, exported);
        Assert.Contains("Backup Org", exported);
        Assert.Contains("Backup key", exported);
        Assert.DoesNotContain(rawKey.Split('.')[1], exported); // no key secret
        Assert.DoesNotContain("hashedSecret", exported, StringComparison.OrdinalIgnoreCase);

        // Lose the account, then restore from the export.
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync("/api/v1/users/me")).StatusCode);
        var gone = await NewClient(factory).SendAsync(Internal(HttpMethod.Get, "/internal/backup/export"));
        Assert.DoesNotContain(email, await gone.Content.ReadAsStringAsync());

        var restore = await NewClient(factory).SendAsync(Internal(HttpMethod.Post, "/internal/backup/restore",
            new StringContent(exported, System.Text.Encoding.UTF8, "application/json")));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var after = await (await NewClient(factory).SendAsync(Internal(HttpMethod.Get, "/internal/backup/export"))).Content.ReadAsStringAsync();
        Assert.Contains(email, after);
        Assert.Contains("Backup Org", after);

        // Running it again changes nothing.
        using var again = JsonDocument.Parse(await (await NewClient(factory).SendAsync(Internal(HttpMethod.Post, "/internal/backup/restore",
            new StringContent(exported, System.Text.Encoding.UTF8, "application/json")))).Content.ReadAsStringAsync());
        Assert.Equal(0, again.RootElement.GetProperty("usersAdded").GetInt32());
    }

    [Fact]
    public async Task EditItems_RemovesAndReplacesListEntries_AndBumpsTheVersion()
    {
        var api = NewClient(factory);
        api.DefaultRequestHeaders.Add("X-Api-Key", ApiConstants.DemoRawKey);

        async Task<JsonDocument> GetAsthma() => JsonDocument.Parse(await api.GetStringAsync("/api/v1/topics/Asthma"));

        using var before = await GetAsthma();
        var symptoms = before.RootElement.GetProperty("symptoms").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.True(symptoms.Count >= 2);
        var version = before.RootElement.GetProperty("version").GetInt32();

        var edits = new object[]
        {
            new { name = "Asthma", field = "observations", item = symptoms[0].ToUpperInvariant() }, // case-insensitive match
            new { name = "Asthma", field = "observations", item = symptoms[1], replaceWith = "Replaced entry" },
            new { name = "Asthma", field = "observations", item = "not a real entry" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/topics/edit-items") { Content = JsonContent.Create(edits) };
        request.Headers.Add("X-Internal-Token", MeducateApiFactory.InternalToken);
        var response = await NewClient(factory).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, result.RootElement.GetProperty("applied").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("notFound").GetInt32());

        // The public endpoint serves a cached copy for a few minutes, so read the stored topic through the internal view.
        using var inspect = new HttpRequestMessage(HttpMethod.Get, "/internal/topics/fact-check?count=10&skip=0");
        inspect.Headers.Add("X-Internal-Token", MeducateApiFactory.InternalToken);
        using var stored = JsonDocument.Parse(await (await NewClient(factory).SendAsync(inspect)).Content.ReadAsStringAsync());
        var asthma = stored.RootElement.GetProperty("topics").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Asthma");
        var now = asthma.GetProperty("observations").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.DoesNotContain(symptoms[0], now);
        Assert.Contains("Replaced entry", now);
        Assert.Equal(version + 1, asthma.GetProperty("version").GetInt32());

        var anonymous = await NewClient(factory).PostAsJsonAsync("/internal/topics/edit-items", edits);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
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
