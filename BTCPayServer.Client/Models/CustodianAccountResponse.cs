using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class CustodianAccountResponse : CustodianAccountData
{
    public IDictionary<string, decimal> AssetBalances { get; set; }

    public CustodianAccountResponse()
    {

    }

}
