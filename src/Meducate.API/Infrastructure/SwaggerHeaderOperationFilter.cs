using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Meducate.API.Infrastructure;

internal sealed class SwaggerHeaderOperationFilter : IOperationFilter
{
    private const string HeaderName = "X-Api-Key";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation == null) return;

        var requiresKey = context.ApiDescription?
            .ActionDescriptor?
            .EndpointMetadata?
            .OfType<RequiresApiKeyAttribute>()
            .Any() == true;

        if (!requiresKey)
            return;

        // Every key-protected route can answer these, whatever its own handler declares.
        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid API key." });
        operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded: 60 requests per minute per key, plus the key's daily limit. See the Retry-After header." });

        operation.Parameters ??= [];

        if (operation.Parameters.Any(p => p.Name == HeaderName))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            Description = "API key in the format keyId.secret"
        });
    }
}
