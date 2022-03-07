using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Custodians;

public interface ICustodian
{
    /**
     * Get the unique code that identifies this custodian.
     */
    public string GetCode();
    
    public string GetName();

    /**
     * Get a list of assets and their qty in custody.
     */
    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken);
}
