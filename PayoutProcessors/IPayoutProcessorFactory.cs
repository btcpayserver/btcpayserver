using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.PayoutProcessors;

public interface IPayoutProcessorFactory
{
    public string Processor { get; }
    public string FriendlyName { get; }
    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request);
    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods();
    public Task<IHostedService> ConstructProcessor(PayoutProcessorData settings);
    public Task<bool> CanRemove() => Task.FromResult(true);
}
