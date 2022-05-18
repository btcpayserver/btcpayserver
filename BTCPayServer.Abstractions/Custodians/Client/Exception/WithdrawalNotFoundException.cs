namespace BTCPayServer.Abstractions.Custodians;

public class WithdrawalNotFoundException : CustodianApiException
{
    private string WithdrawalId { get; }

    public WithdrawalNotFoundException(string withdrawalId) : base(404, "withdrawal-not-found", $"Could not find withdrawal ID {withdrawalId}.")
    {
        WithdrawalId = withdrawalId;
    }
}
