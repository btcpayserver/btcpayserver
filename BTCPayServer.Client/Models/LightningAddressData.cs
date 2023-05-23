namespace BTCPayServer.Client.Models;

public class LightningAddressData
{
    public string Username { get; set; }
    public string CurrencyCode { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }

}
