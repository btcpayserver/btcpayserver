using BTCPayServer.Data;

namespace BTCPayServer.Services.Custodian;

public interface ICanDeposit
{
    /**
     * Get the address where we can deposit for the chosen payment method (crypto code + network).
     * The result can be a string in different formats like a bitcoin address or even a LN invoice.
     */
    public DepositAddressData GetDepositAddress(string paymentMethod);

    public string[] GetDepositablePaymentMethods();
}
