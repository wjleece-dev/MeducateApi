
using Meducate.Web.Components;
using Meducate.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    // Only Cloudflare and Railway's edge can reach this app, so trust their forwarded scheme. Without this the
    // app saw plain http: no HSTS header and http:// redirect locations.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ToastService>();

builder.Services.AddTransient<CookieForwardingHandler>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
    throw new InvalidOperationException("Api:BaseUrl is not configured. Set it in appsettings, user secrets, or environment variables.");

builder.Services.AddHttpClient("SwaggerProxy", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["Api:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<ApiService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    client.BaseAddress = new Uri(config["Api:BaseUrl"]!);

    client.DefaultRequestHeaders.Add("X-Requested-By", "MeducateAPI");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<CookieForwardingHandler>();

builder.Services.AddHttpClient("VerifyProxy", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["Api:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-Requested-By", "MeducateAPI");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = false
});

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MeducateAPI");

app.UseForwardedHeaders();
app.UseStatusCodePagesWithReExecute("/not-found");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800"; // 7 days
    }
});
app.UseHttpsRedirection();

// Redirect www to canonical non-www (must be after HTTPS redirect so the redirect URL is always https)
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var nonWwwHost = host[4..];
        var port = context.Request.Host.Port;
        var hostString = port.HasValue ? $"{nonWwwHost}:{port}" : nonWwwHost;
        var redirectUrl = $"{context.Request.Scheme}://{hostString}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(redirectUrl, permanent: true);
        return;
    }
    await next();
});

var isDevelopment = app.Environment.IsDevelopment();

app.Use(async (ctx, next) =>
{
    var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var apiUrl = config["Api:PublicUrl"] ?? config["Api:BaseUrl"] ?? "";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    var connectSrc = $"'self' https://unpkg.com {apiUrl}";
    if (isDevelopment)
        connectSrc += " ws: wss: http://localhost:*";

    ctx.Response.Headers["Content-Security-Policy"] =
        $"default-src 'self'; script-src 'self' 'unsafe-inline' https://unpkg.com; style-src 'self' 'unsafe-inline' https://unpkg.com; img-src 'self' data:; connect-src {connectSrc}; frame-ancestors 'none'";
    await next();
});

app.UseAntiforgery();

app.MapGet("/auth/verify", async (string? token, IHttpClientFactory clientFactory, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(token))
        return Results.Redirect("/verify");

    logger.LogInformation("[/auth/verify] Proxying token to API...");

    var client = clientFactory.CreateClient("VerifyProxy");
    using var response = await client.PostAsJsonAsync("/api/v1/users/verify", new { token });

    logger.LogInformation("[/auth/verify] API status: {StatusCode}", (int)response.StatusCode);

    // Forward the auth cookie from the API response to the browser
    var cookieCount = 0;
    if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
    {
        foreach (var cookie in cookies)
        {
            http.Response.Headers.Append("Set-Cookie", cookie);
            cookieCount++;
        }
    }
    logger.LogInformation("[/auth/verify] Forwarded {CookieCount} Set-Cookie header(s)", cookieCount);

    var json = await response.Content.ReadAsStringAsync();

    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (response.IsSuccessStatusCode)
    {
        logger.LogInformation("[/auth/verify] Success — redirecting to /create-organisation");
        return Results.Redirect("/create-organisation");
    }

    // Error responses use RFC 7807 Problem Details with a "detail" field
    var message = root.TryGetProperty("detail", out var d)
        ? d.GetString() ?? "Verification failed."
        : "Verification failed.";

    logger.LogWarning("[/auth/verify] Failed — redirecting to /verify with message: {Message}", message);
    return Results.Redirect($"/verify?message={Uri.EscapeDataString(message)}");
});

app.MapGet("/auth/logout", (HttpContext http) =>
{
    // Delete the auth cookie
    http.Response.Cookies.Delete("meducateapi_auth", new CookieOptions
    {
        Path = "/",
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict
    });

    // Static HTML to break Blazor circuit, then immediate redirect
    return Results.Content("""
        <!DOCTYPE html>
        <html>
        <head><script>window.location.replace('/register');</script></head>
        <body></body>
        </html>
        """, "text/html");
});

app.MapGet("/api-docs/swagger.json", async (IConfiguration config, IHttpClientFactory clientFactory, ILogger<Program> log) =>
{
    var baseUrl = config["Api:PublicUrl"] ?? config["Api:BaseUrl"]!;

    var client = clientFactory.CreateClient("SwaggerProxy");

    HttpResponseMessage? response = null;
    try
    {
        response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
    }
    catch (Exception ex)
    {
        response?.Dispose();
        log.LogWarning(ex, "Failed to fetch Swagger spec from API");
        return Results.Problem(
            detail: "API documentation is temporarily unavailable.",
            statusCode: StatusCodes.Status502BadGateway);
    }

    using (response)
    {
        var json = await response.Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "paths")
                {
                    writer.WriteStartObject("paths");
                    foreach (var path in prop.Value.EnumerateObject())
                    {
                        if (!path.Name.StartsWith("/api/v1/topics"))
                            continue;

                        writer.WritePropertyName(path.Name);
                        writer.WriteStartObject();
                        foreach (var method in path.Value.EnumerateObject())
                        {
                            writer.WritePropertyName(method.Name);
                            writer.WriteStartObject();
                            foreach (var field in method.Value.EnumerateObject())
                            {
                                if (field.Name == "tags")
                                {
                                    writer.WriteStartArray("tags");
                                    writer.WriteStringValue("Topics");
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    field.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                else if (prop.Name == "tags")
                {
                    // Replace all tags with a single "Topics" tag
                    writer.WriteStartArray("tags");
                    writer.WriteStartObject();
                    writer.WriteString("name", "Topics");
                    writer.WriteString("description", "Health topic endpoints");
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }
                else if (prop.Name == "servers")
                {
                    // Point Swagger UI requests at the actual API
                    writer.WriteStartArray("servers");
                    writer.WriteStartObject();
                    writer.WriteString("url", baseUrl.TrimEnd('/'));
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            // Add servers if the original spec didn't have one
            if (!doc.RootElement.TryGetProperty("servers", out _))
            {
                writer.WriteStartArray("servers");
                writer.WriteStartObject();
                writer.WriteString("url", baseUrl.TrimEnd('/'));
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Results.Content(
            System.Text.Encoding.UTF8.GetString(stream.ToArray()),
            "application/json");
    }
});

app.MapMethods("/health", ["GET", "HEAD"], () => Results.Ok());

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = "'none'");

app.Run();