using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Custodians;

public interface ICanDeposit
{
    /**
     * Get the address where we can deposit for the chosen payment method (crypto code + network).
     * The result can be a string in different formats like a bitcoin address or even a LN invoice.
     */
    public Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config, CancellationToken cancellationToken);

    public string[] GetDepositablePaymentMethods();
}
