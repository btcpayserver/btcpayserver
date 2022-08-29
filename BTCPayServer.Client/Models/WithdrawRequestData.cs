namespace BTCPayServer.Client.Models;

public class WithdrawRequestData
{
    public string PaymentMethod { set; get; }
    public string Qty { set; get; }

    public WithdrawRequestData()
    {
        
    }
    
    public WithdrawRequestData(string paymentMethod, string qty)
    {
        PaymentMethod = paymentMethod;
        Qty = qty;
    }
}
