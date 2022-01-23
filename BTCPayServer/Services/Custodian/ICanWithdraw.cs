using BTCPayServer.Data;

namespace BTCPayServer.Services.Custodian;

public interface ICanWithdraw
{
    public WithdrawResultData withdraw(string paymentMethod, decimal amount, WithdrawalTarget target);
}
