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
    public void InvoiceStatusMutationsRequireManageStatusPermission()
    {
        AssertPolicy(typeof(UIInvoiceController), nameof(UIInvoiceController.ChangeInvoiceState), Policies.CanManageInvoiceStatus);
        AssertPolicy(typeof(GreenfieldInvoiceController), nameof(GreenfieldInvoiceController.MarkInvoiceStatus), Policies.CanManageInvoiceStatus);
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

    [Fact]
    [Trait("Fast", "Fast")]
    public void FileUploadRequiresAntiforgeryValidation()
    {
        var method = typeof(UIAppsController).GetMethod(nameof(UIAppsController.FileUpload));
        Assert.NotNull(method);
        Assert.Empty(method.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(true));
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void ServerFileDeletionRequiresPostWithAntiforgeryValidation()
    {
        var method = typeof(UIServerController).GetMethod(nameof(UIServerController.DeleteFile));
        Assert.NotNull(method);
        Assert.NotEmpty(method.GetCustomAttributes<HttpPostAttribute>(true));
        Assert.Empty(method.GetCustomAttributes<HttpGetAttribute>(true));
        Assert.Empty(method.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(true));
    }
}
