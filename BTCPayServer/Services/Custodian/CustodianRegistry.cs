using System.Collections.Generic;

namespace BTCPayServer.Services.Custodian;

public class CustodianRegistry
{
    private IDictionary<string, ICustodian> _custodians;

    public CustodianRegistry()
    {
        _custodians = new Dictionary<string, ICustodian>();

        // TODO We should add a hook here so plugins can register their own custodians!
        register(Kraken.getInstance());

        // TODO register a dummy custodian when/for running tests
    }

    public void register(ICustodian custodian)
    {
        _custodians.Add(custodian.getCode(), custodian);
    }

    public IDictionary<string, ICustodian> getAll()
    {
        return _custodians;
    }
}
