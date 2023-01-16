namespace BTCPayServer.Client.Models;

public class DepositAddressData
{
    // /**
    // * Example: P2PKH, P2SH, P2WPKH, P2TR, BOLT11, ...
    // */
    // public string Type { get; set; }

    /**
     * Format depends hugely on the type.
     */
    public string Address { get; set; }

}
