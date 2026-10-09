using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Routing;
using BTCPayServer.Hosting;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Plugins.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BTCPayServer.Tests;

public class RegexRouteConventionTests
{
    [Fact]
    public void PluginsRegisterExpectedRouteIdConventions()
    {
        var services = new ServiceCollection();
        new BTCPayServerPlugin().Execute(services);
        new WebhooksPlugin().Execute(services);
        new SubscriptionsPlugin().Execute(services);

        var conventions = services
            .Where(d => d.ServiceType == typeof(RegexRouteConvention))
            .Select(d => Assert.IsType<RegexRouteConvention>(d.ImplementationInstance))
            .ToDictionary(c => c.RouteParameterName, StringComparer.Ordinal);

        var examples = new Dictionary<string, (string Valid, string Invalid)>
        {
            ["apiKeyId"] = ("akid_0123456789abcdef", "akid_0123456789abcdeF"),
            ["storeId"] = ("123Abcz", "1230Abcz"),
            ["invoiceId"] = ("123Abcz", "1230Abcz"),
            ["appId"] = ("123Abcz", "1230Abcz"),
            ["pullPaymentId"] = ("123Abcz", "1230Abcz"),
            ["payoutId"] = ("123Abcz", "1230Abcz"),
            ["paymentRequestId"] = ("01234567-89ab-CDEF-0123-456789abcdef", "0123456789abcdef"),
            ["paymentHash"] = (new string('A', 64), new string('A', 63)),
            ["webhookId"] = ("123Abcz", "1230Abcz"),
            ["deliveryId"] = ("123Abcz", "1230Abcz"),
            ["offeringId"] = ("offering_123Abcz", "offering_1230Abcz"),
            ["planId"] = ("plan_123Abcz", "offering_123Abcz"),
            ["checkoutId"] = ("plancheckout_123Abcz", "plancheckout_1230Abcz"),
            ["portalSessionId"] = ("ps_123Abcz", "ps_1230Abcz")
        };

        Assert.Equal(examples.Keys.Order(), conventions.Keys.Order());
        foreach (var (name, example) in examples)
        {
            Assert.Matches(conventions[name].Regex, example.Valid);
            Assert.DoesNotMatch(conventions[name].Regex, example.Invalid);
            Assert.DoesNotMatch(conventions[name].Regex, example.Valid + "\n");
        }
    }

    [Fact]
    public void InvalidRouteIdRegexFailsAtRegistration()
    {
        var services = new ServiceCollection();
        Assert.Throws<RegexParseException>(() =>
            services.AddRegexRouteConvention("otherId", "["));
    }

    [Fact]
    public void RouteConstraintEnforcesOutboundValues()
    {
        var convention = new RegexRouteConvention("entityId", "^[0-9]+$");
        var constraint = new RegexRouteConstraint([convention]);
        var values = new RouteValueDictionary { ["entityId"] = "123" };

        Assert.True(constraint.Match(new DefaultHttpContext(), null, "entityId", values,
            RouteDirection.UrlGeneration));
        values["entityId"] = "invalid";
        Assert.False(constraint.Match(new DefaultHttpContext(), null, "entityId", values,
            RouteDirection.UrlGeneration));
    }
}

[ApiController]
public class RegexRouteConventionTestController : ControllerBase
{
    public const string RouteName = nameof(RegexRouteConventionTestController);
    public const string CaseSensitiveRouteName = RouteName + nameof(CaseSensitiveRouteName);

    [HttpGet("~/route-convention-test/{apiKeyId}", Name = RouteName)]
    public IActionResult Get()
    {
        return Ok();
    }

    [HttpGet("~/route-convention-case-test/{apikeyid}", Name = CaseSensitiveRouteName)]
    public IActionResult GetCaseSensitive()
    {
        return Ok();
    }
}
