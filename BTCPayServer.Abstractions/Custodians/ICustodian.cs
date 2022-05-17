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
    string Code { get; }
    
    string Name { get; }

    /**
     * Get a list of assets and their qty in custody.
     */
    Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken);
}
