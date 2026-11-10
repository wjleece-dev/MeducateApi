
namespace Meducate.Web.Models;

internal class ApiResult
{
    public bool IsSuccess { get; init; }
    public string? Message { get; init; }
}

internal sealed class ApiResult<T> : ApiResult
{
    public T? Data { get; init; }
}
