namespace BTCPayServer.Data;

public class WithdrawalTarget
{
    /**
     * Example:
     * - "AddressBook" if the exchange wants you to withdraw to a stored address from your address book
     * - "BOLT11" for a LN withdrawal
     * - ...
     */
    public string Type { get; set; }
    
    /**
     * Format depends hugely on the type.
     */
    public string Address { get; set; }
}
