using System.Collections.Generic;
using System.Net.Http;

namespace BTCPayServer.Services.Custodian.Client;

public class CustodianRegistry
{
    private IDictionary<string, ICustodian> _custodians;

    public CustodianRegistry(IHttpClientFactory httpClientFactory)
    {
        _custodians = new Dictionary<string, ICustodian>();

        // TODO Dispatch event so plugins can register their own custodians?
        // TODO register a dummy custodian when/for running tests?
        register(new KrakenClient(httpClientFactory));
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
