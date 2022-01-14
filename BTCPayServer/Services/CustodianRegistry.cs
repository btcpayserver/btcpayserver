using System.Collections.Generic;
using BTCPayServer.Services.Custodian;

namespace BTCPayServer.Services;

public class CustodianService
{
    private IDictionary<string, ICustodian> _custodians;

    public void register(ICustodian custodian)
    {
        _custodians.Add(custodian.getCode(), custodian);
    }
    
}
