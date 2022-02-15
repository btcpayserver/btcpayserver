using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Custodian.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian;

public interface ICanWithdraw
{
    public Task<WithdrawResult> WithdrawAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken);

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config, CancellationToken cancellationToken);

    public string[] GetWithdrawablePaymentMethods();
}
