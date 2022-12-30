using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Form;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Custodians.FakeCustodian;

public class FakeCustodian : ICustodian
{
    public string Code
    {
        get => "fake";
    }

    public string Name
    {
        get => "Fake Exchange";
    }

    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken)
    {
        var fakeConfig = ParseConfig(config);
        var r = new Dictionary<string, decimal>()
        {
            { "BTC", fakeConfig.BTCBalance },
            { "LTC", fakeConfig.LTCBalance },
            { "USD", fakeConfig.USDBalance },
            { "EUR", fakeConfig.EURBalance }
        };
        return Task.FromResult(r);
    }

    public Task<Form> GetConfigForm(JObject config, string locale, CancellationToken cancellationToken = default)
    {
        var fakeConfig = ParseConfig(config);

        var form = new Form();
        var fieldset = Field.CreateFieldset();

        // Maybe a decimal type field would be better?
        var fakeBTCBalance = Field.Create("BTC Balance", "BTCBalance", fakeConfig?.BTCBalance.ToString(), true,
            "Enter the amount of BTC you want to have.");
        var fakeLTCBalance = Field.Create("LTC Balance", "LTCBalance", fakeConfig?.LTCBalance.ToString(), true,
            "Enter the amount of LTC you want to have.");
        var fakeEURBalance = Field.Create("EUR Balance", "EURBalance", fakeConfig?.EURBalance.ToString(), true,
            "Enter the amount of EUR you want to have.");
        var fakeUSDBalance = Field.Create("USD Balance", "USDBalance", fakeConfig?.USDBalance.ToString(), true,
            "Enter the amount of USD you want to have.");

        fieldset.Label = "Your fake balances";
        fieldset.Fields.Add(fakeBTCBalance);
        fieldset.Fields.Add(fakeLTCBalance);
        fieldset.Fields.Add(fakeEURBalance);
        fieldset.Fields.Add(fakeUSDBalance);
        form.Fields.Add(fieldset);

        return Task.FromResult(form);
    }

    private FakeCustodianConfig ParseConfig(JObject config)
    {
        return config?.ToObject<FakeCustodianConfig>() ?? throw new InvalidOperationException("Invalid config");
    }
}

public class FakeCustodianConfig
{
    public decimal BTCBalance { get; set; }
    public decimal LTCBalance { get; set; }
    public decimal USDBalance { get; set; }
    public decimal EURBalance { get; set; }

    public FakeCustodianConfig()
    {
    }
}
