#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
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

    public Task<Form.Form> GetConfigForm(JObject config, CancellationToken cancellationToken = default);

}
