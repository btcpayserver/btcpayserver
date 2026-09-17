using Xunit;

namespace BTCPayServer.Tests;

public class LightningConnectionStringSafetyTests
{
    [Theory]
    [InlineData("type=phoenixd;server=https://example.com;passwordfilepath=/run/secrets/phoenixd.pwd", false)]
    [InlineData("type=lnd-rest;server=https://example.com;customFilePath=/run/secrets/value", false)]
    [InlineData("type=lnd-rest;server=https://example.com;customDirectoryPath=/run/secrets", false)]
    [InlineData("type=phoenixd;server=https://example.com;filepathvalue=/run/secrets/value", true)]
    [InlineData("type=phoenixd;server=https://example.com;password=secret", true)]
    [InlineData("type=phoenixd;server=http://127.0.0.1:9740;password=secret", false)]
    public void DetectsUnsafeLightningConnectionStrings(string connectionString, bool expected)
    {
        Assert.Equal(expected, BTCPayServer.Extensions.IsSafeLightningConnectionString(connectionString));
    }
}
