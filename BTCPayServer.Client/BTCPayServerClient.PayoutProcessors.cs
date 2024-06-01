#nullable enable
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<PayoutProcessorData>> GetPayoutProcessors(CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<PayoutProcessorData>>("api/v1/payout-processors", null, HttpMethod.Get, token);
    }
}
