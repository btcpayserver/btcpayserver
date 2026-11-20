using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Blazor;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BTCPayServer.Tests;

public class CircuitUserRevalidatorTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Fast", "Fast")]
    public async Task RevalidateRefusesPrincipalWithoutAuthenticatedIdentity()
    {
        var revalidator = new CircuitUserRevalidator(new UnusableScopeFactory());

        Assert.Null(await revalidator.Revalidate(null));
        Assert.Null(await revalidator.Revalidate(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(await revalidator.Revalidate(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "someone")]))));
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Integration", "Integration")]
    public async Task RevalidateRefusesCircuitWithStaleAdminRole()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var account = tester.NewAccount();
        await account.RegisterAsync(isAdmin: true);
        await using var scope = tester.PayTester.ServiceProvider.CreateAsyncScope();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await signInManager.UserManager.FindByIdAsync(account.UserId);
        var adminPrincipal = await signInManager.CreateUserPrincipalAsync(user);
        Assert.True(adminPrincipal.IsInRole(Roles.ServerAdmin));
        var revalidator = scope.ServiceProvider.GetRequiredService<CircuitUserRevalidator>();
        Assert.NotNull(await revalidator.Revalidate(adminPrincipal));

        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        Assert.True(await userService.SetAdminUser(account.UserId, false));
        Assert.Null(await revalidator.Revalidate(adminPrincipal));
    }

    private class UnusableScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            Assert.Fail("An unauthenticated principal must be refused before any service is resolved.");
            return null;
        }
    }
}
