using System.Security.Claims;
using Meducate.API.Endpoints;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class EndpointHelpersTests
{
    [Fact]
    public async Task GetVerifiedUserAsync_NoNameIdentifier_ReturnsError()
    {
        var http = new DefaultHttpContext();

        var (user, error) = await EndpointHelpers.GetVerifiedUserAsync(http, new StubUserRepository(), CancellationToken.None);

        Assert.Null(user);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetVerifiedUserAsync_InvalidGuid_ReturnsError()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-guid")]));

        var (user, error) = await EndpointHelpers.GetVerifiedUserAsync(http, new StubUserRepository(), CancellationToken.None);

        Assert.Null(user);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetVerifiedUserAsync_UserNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

        var (user, error) = await EndpointHelpers.GetVerifiedUserAsync(http, new StubUserRepository(), CancellationToken.None);

        Assert.Null(user);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetVerifiedUserAsync_UnverifiedUser_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

        var repo = new StubUserRepository { UserToReturn = new User { Email = "test@example.com" } };
        // IsEmailVerified defaults to false

        var (user, error) = await EndpointHelpers.GetVerifiedUserAsync(http, repo, CancellationToken.None);

        Assert.Null(user);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetVerifiedUserAsync_VerifiedUser_ReturnsUser()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

        var testUser = new User { Email = "test@example.com", IsEmailVerified = true };
        var repo = new StubUserRepository { UserToReturn = testUser };

        var (user, error) = await EndpointHelpers.GetVerifiedUserAsync(http, repo, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Null(error);
        Assert.Equal("test@example.com", user.Email);
    }

    [Fact]
    public async Task GetOwnedOrgAsync_OrgNotFound_ReturnsError()
    {
        var user = new User { Email = "test@example.com" };
        var repo = new StubOrgRepository();

        var (org, error) = await EndpointHelpers.GetOwnedOrgAsync(Guid.NewGuid(), user, repo, CancellationToken.None);

        Assert.Null(org);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetOwnedOrgAsync_OrgBelongsToOtherUser_ReturnsError()
    {
        var user = new User { Email = "test@example.com" };
        var otherUserId = Guid.NewGuid();
        var org = new Organisation { Name = "Test Org", UserId = otherUserId };
        var repo = new StubOrgRepository { OrgToReturn = org };

        var (result, error) = await EndpointHelpers.GetOwnedOrgAsync(org.Id, user, repo, CancellationToken.None);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetOwnedOrgAsync_OrgBelongsToUser_ReturnsOrg()
    {
        var user = new User { Email = "test@example.com" };
        var org = new Organisation { Name = "Test Org", UserId = user.Id };
        var repo = new StubOrgRepository { OrgToReturn = org };

        var (result, error) = await EndpointHelpers.GetOwnedOrgAsync(org.Id, user, repo, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(error);
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public User? UserToReturn { get; set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(UserToReturn);

        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(UserToReturn);

        public Task<User> GetOrCreateAsync(string email, CancellationToken ct = default)
            => Task.FromResult(UserToReturn ?? new User { Email = email });

        public Task<User?> GetByVerificationTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult<User?>(null);

        public Task<User?> GetByVerificationTokenIncludingExpiredAsync(string token, CancellationToken ct = default)
            => Task.FromResult<User?>(null);

        public Task VerifyAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubOrgRepository : IOrganisationRepository
    {
        public Organisation? OrgToReturn { get; set; }

        public Task<Organisation?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(OrgToReturn);

        public Task<Organisation?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(OrgToReturn);

        public Task<Organisation> CreateAsync(string name, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new Organisation { Name = name, UserId = userId });
    }
}
