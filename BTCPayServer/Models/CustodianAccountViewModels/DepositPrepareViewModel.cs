namespace BTCPayServer.Models.CustodianAccountViewModels;

public class DepositPrepareViewModel
{
    public string PaymentMethod { get; set; }
    public string Address { get; set; }
    public string AddressQRHtml { get; set; }
    public string Link { get; set; }
    public string LinkQRHtml { get; set; }
    public string CryptoImageUrl { get; set; }
    public string ErrorMessage { get; set; }
}
