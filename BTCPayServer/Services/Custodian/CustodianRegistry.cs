using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Services.Custodian.Client.Kraken;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Services.Custodian.Client;

public class CustodianRegistry
{
    private IDictionary<string, ICustodian> _custodians;

    public CustodianRegistry(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
    {
        _custodians = new Dictionary<string, ICustodian>();

        // TODO Dispatch event so plugins can register their own custodians?
        // TODO register a dummy custodian when/for running tests?
        register(new KrakenExchange(httpClientFactory.CreateClient(), memoryCache));
    }

    public void register(ICustodian custodian)
    {
        _custodians.Add(custodian.GetCode(), custodian);
    }

    public IDictionary<string, ICustodian> getAll()
    {
        return _custodians;
    }
}
