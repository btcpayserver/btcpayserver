namespace BTCPayServer.Services.Custodian.Client.Kraken;

public class KrakenConfig
{
    public string ApiKey { get; set; }
    public string PrivateKey { get; set; }
    public string WithdrawToAddressName { get; set; }

    public KrakenConfig()
    {
    }

    public KrakenConfig(string ApiKey, string PrivateKey, string WithdrawToAddressName)
    {
        this.ApiKey = ApiKey;
        this.PrivateKey = PrivateKey;
        this.WithdrawToAddressName = WithdrawToAddressName;
    }

}
