using Meducate.API.Infrastructure;

namespace Meducate.Tests;

public class PaginatedResponseTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var items = new[] { "a", "b", "c" };

        var response = new PaginatedResponse<string>(items, 100, skip: 0, take: 10);

        Assert.Equal(items, response.Items);
        Assert.Equal(100, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(10, response.TotalPages);
        Assert.True(response.HasNextPage);
    }

    [Fact]
    public void EmptyItems_WorksCorrectly()
    {
        var response = new PaginatedResponse<int>([], 0, skip: 0, take: 50);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(50, response.PageSize);
        Assert.Equal(0, response.TotalPages);
        Assert.False(response.HasNextPage);
    }

    [Fact]
    public void Page_ComputedFromSkipAndTake()
    {
        var response = new PaginatedResponse<int>([1, 2], 100, skip: 20, take: 10);

        Assert.Equal(3, response.Page);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(10, response.TotalPages);
        Assert.True(response.HasNextPage);
    }

    [Fact]
    public void LastPage_HasNextPageIsFalse()
    {
        var response = new PaginatedResponse<int>([1], 25, skip: 20, take: 5);

        Assert.Equal(5, response.Page);
        Assert.Equal(5, response.TotalPages);
        Assert.False(response.HasNextPage);
    }

    [Fact]
    public void DefaultSkipAndTake_UsesTotalCountAsPageSize()
    {
        var response = new PaginatedResponse<string>(["a"], 10);

        Assert.Equal(1, response.Page);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(1, response.TotalPages);
        Assert.False(response.HasNextPage);
    }
}
