namespace Meducate.Web.Services;

internal sealed class CookieForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private const string AuthCookieName = "meducateapi_auth";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext?.Request.Cookies.TryGetValue(AuthCookieName, out var authCookie) == true)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{AuthCookieName}={authCookie}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
