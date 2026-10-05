using System.Collections.Generic;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BTCPayServer.Tests;

public class LightningConnectionStringSafetyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void LoadsSSRFProtectionConfiguration(string configuredValue, bool expected)
    {
        var values = new Dictionary<string, string> { ["network"] = "regtest" };
        if (configuredValue is not null)
            values["disablessrfprotection"] = configuredValue;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new BTCPayServerOptions();

        options.LoadArgs(configuration, new Logs());

        Assert.Equal(expected, options.DisableSSRFProtection);
    }

    [Theory]
    [InlineData("type=phoenixd;server=https://example.com;passwordfilepath=/run/secrets/phoenixd.pwd", false)]
    [InlineData("type=phoenixd;server=https://example.com;PassWordFilePath=/run/secrets/phoenixd.pwd", false)]
    [InlineData("type=lnd-rest;server=https://example.com;customFilePath=/run/secrets/value", false)]
    [InlineData("type=lnd-rest;server=https://example.com;customDirectoryPath=/run/secrets", false)]
    [InlineData("type=phoenixd;server=https://example.com;filepathvalue=/run/secrets/value", true)]
    [InlineData("type=phoenixd;server=https://example.com;password=secret", true)]
    [InlineData("type=lnd-rest;server=https://example.com;allowinsecure=true", false)]
    [InlineData("type=lnd-rest;server=http://8.8.8.8;allowinsecure=true", true)]
    [InlineData("type=lnd-rest;server=https://example.com;certthumbprint=0000000000000000000000000000000000000000000000000000000000000000", false)]
    [InlineData("type=phoenixd;server=http://127.0.0.1:9740;password=secret", false)]
    [InlineData("lndhub://user:pass@https://8.8.8.8", true)]
    [InlineData("lndhub://user:pass@http://8.8.8.8", true)]
    [InlineData("lndhub://user:pass@http://127.0.0.1:3000", false)]
    [InlineData("lndhub://user:pass@http://10.0.0.1:3000", false)]
    [InlineData("type=phoenixd;password=secret", false)]
    [InlineData("type=clightning;server=tcp://example.com:9735", false)]
    [InlineData("type=clightning;server=tcp://8.8.8.8:9735", true)]
    public void DetectsUnsafeLightningConnectionStrings(string connectionString, bool expected)
    {
        Assert.Equal(expected, BTCPayServer.Extensions.IsSafeLightningConnectionString(connectionString));
    }
}
