namespace BTCPayServer.Client.Models;

public class WithdrawRequestData
{
    public string PaymentMethod { set; get; }
    public decimal Qty { set; get; }

    public WithdrawRequestData(string paymentMethod, decimal qty)
    {
        PaymentMethod = paymentMethod;
        Qty = qty;
    }
}
