using System.IO.Compression;
using System.Threading.RateLimiting;
using Meducate.API.Infrastructure;
using Meducate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Meducate.API.DependencyInjection;

internal static class ApiServiceRegistration
{
    private const long MaxRequestBodySize = 1_048_576; // 1 MB

    internal static IServiceCollection AddMeducateApi(this IServiceCollection services, IConfiguration config, IWebHostEnvironment? env = null)
    {
        // Request size limits
        services.Configure<KestrelServerOptions>(options =>
            options.Limits.MaxRequestBodySize = MaxRequestBodySize);
        services.Configure<FormOptions>(options =>
            options.MultipartBodyLengthLimit = MaxRequestBodySize);

        // Response compression
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.SmallestSize);

        // JSON serialization — contextual field names based on TopicType
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new HealthTopicJsonConverter()));

        // Swagger
        services.AddEndpointsApiExplorer();
        services.AddSingleton<ApiKeyRateLimiter>();
        services.AddProblemDetails();

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
            {
                Title = "Meducate API",
                Version = "v1",
                Description = "Medical education API providing structured, categorised health topics refreshed daily from MedlinePlus and PubMed.",
                Contact = new Microsoft.OpenApi.OpenApiContact
                {
                    Name = "Meducate API",
                    Url = new Uri("https://meducateapi.com")
                }
            });
            c.OperationFilter<SwaggerHeaderOperationFilter>();
            c.SchemaFilter<HealthTopicSchemaFilter>();
        });

        // Rate limiting
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, ct) =>
            {
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)retryAfter.TotalSeconds
                    : 60;

                var isWaitlist = context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName == ApiConstants.WaitlistRateLimitPolicy;

                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = isWaitlist
                        ? $"Too many waitlist requests from this address. Retry in {retryAfterSeconds} seconds."
                        : $"Too many requests from this address. Retry in {retryAfterSeconds} seconds.",
                }, ct);
            };

            options.GlobalLimiter =
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    // Pre-authentication abuse guard, per client address. The per-key limit (60/min)
                    // is applied after authentication by ApiKeyMiddleware.
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"ip:{ClientIp.Resolve(context)}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 300,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });

            // Waitlist: 5 submissions per hour per client address. Stacks with the global limiter.
            options.AddPolicy(ApiConstants.WaitlistRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"waitlist:{ClientIp.Resolve(context)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        // CORS
        var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var isDevelopment = env?.IsDevelopment() ?? false;

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowCredentials();
                }
                else if (isDevelopment)
                {
                    // In development, allow any localhost origin
                    policy.SetIsOriginAllowed(origin =>
                    {
                        var uri = new Uri(origin);
                        return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                    })
                    .AllowCredentials();
                }
                else
                {
                    throw new InvalidOperationException(
                        "CORS origins are not configured. Set Cors:AllowedOrigins in appsettings or user secrets.");
                }

                policy
                    .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders("Content-Type", "Accept", "X-Api-Key", "X-Requested-By", "X-Correlation-Id")
                    .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset", "X-Correlation-Id");
            });
        });

        // HSTS
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

        // Health checks
        services.AddHealthChecks()
            .AddDbContextCheck<MeducateDbContext>("database", tags: ["ready"]);
        services.AddHttpContextAccessor();

        return services;
    }
}
