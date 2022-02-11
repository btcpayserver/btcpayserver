using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian;

public interface ICanWithdraw
{
    public Task<WithdrawResult> WithdrawAsync(string asset, decimal amount, JObject config);

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string asset, string withdrawalId, JObject config);
}
