using System.Collections.Generic;

namespace BTCPayServer.Services.Custodian.Client.Kraken;

public class KrakenConfig
{
    public string ApiKey { get; set; }
    public string PrivateKey { get; set; }
    public Dictionary<string,string> WithdrawToAddressNamePerCurrency { get; set; }

    public KrakenConfig()
    {
    }

    public KrakenConfig(string ApiKey, string PrivateKey, Dictionary<string,string> WithdrawToAddressNamePerCurrency)
    {
        this.ApiKey = ApiKey;
        this.PrivateKey = PrivateKey;
        this.WithdrawToAddressNamePerCurrency = WithdrawToAddressNamePerCurrency;
    }

}
