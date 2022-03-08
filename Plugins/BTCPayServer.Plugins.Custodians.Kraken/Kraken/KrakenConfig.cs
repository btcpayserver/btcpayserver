namespace BTCPayServer.Plugins.Custodians.Kraken.Kraken;

public class KrakenConfig
{
    public string ApiKey { get; set; }
    public string PrivateKey { get; set; }
    public Dictionary<string,string> WithdrawToAddressNamePerPaymentMethod { get; set; }

    public KrakenConfig()
    {
    }

    public KrakenConfig(string ApiKey, string PrivateKey, Dictionary<string,string> withdrawToAddressNamePerPaymentMethod)
    {
        this.ApiKey = ApiKey;
        this.PrivateKey = PrivateKey;
        this.WithdrawToAddressNamePerPaymentMethod = withdrawToAddressNamePerPaymentMethod;
    }

}
