using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.Email;

internal sealed class ResendRequestLoggingHandler(ILogger<ResendRequestLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Resend request {Method} {Uri}", request.Method, request.RequestUri);

        var response = await base.SendAsync(request, cancellationToken);

        logger.LogDebug("Resend response {StatusCode}", (int)response.StatusCode);

        return response;
    }
}
