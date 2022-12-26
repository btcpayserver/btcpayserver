using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Custodians.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Custodians;

/// <summary>
/// Interface for custodians that can move funds to the store wallet.
/// </summary>
public interface ICanWithdraw
{
    public Task<WithdrawResult> WithdrawToStoreWalletAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken);

    public Task<SimulateWithdrawalResult> SimulateWithdrawalAsync(string paymentMethod, decimal qty, JObject config, CancellationToken cancellationToken);

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config, CancellationToken cancellationToken);

    public string[] GetWithdrawablePaymentMethods();
}
