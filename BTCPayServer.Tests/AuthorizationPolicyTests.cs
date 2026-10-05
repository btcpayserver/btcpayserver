using System;
using System.Linq;
using System.Reflection;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Controllers.Greenfield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BTCPayServer.Tests;

public class AuthorizationPolicyTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    private static void AssertPolicy(Type controller, string action, string expectedPolicy)
    {
        var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == action)
            .ToArray();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var policies = method.GetCustomAttributes<AuthorizeAttribute>(true)
                .Select(a => a.Policy)
                .Where(p => p is not null)
                .ToArray();
            Assert.Contains(expectedPolicy, policies);
        }
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void InvoiceStatusMutationsRequireModifyPermission()
    {
        AssertPolicy(typeof(UIInvoiceController), nameof(UIInvoiceController.ChangeInvoiceState), Policies.CanModifyInvoices);
        AssertPolicy(typeof(GreenfieldInvoiceController), nameof(GreenfieldInvoiceController.MarkInvoiceStatus), Policies.CanModifyInvoices);
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void ChangeInvoiceStateRequiresAntiforgeryValidation()
    {
        var methods = typeof(UIInvoiceController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(UIInvoiceController.ChangeInvoiceState))
            .ToArray();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
            Assert.Empty(method.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(true));
    }
}
