namespace BTCPayServer.Abstractions.Custodians;

public class CannotWithdrawException : CustodianApiException

{
    public CannotWithdrawException(ICustodian custodian, string paymentMethod, string message) : base(403, "cannot-withdraw", message)
    {
    }

    public CannotWithdrawException(ICustodian custodian, string paymentMethod, string targetAddress, CustodianApiException originalException) : base(403, "cannot-withdraw", $"{custodian.Name} cannot withdraw {paymentMethod} to '{targetAddress}': {originalException.Message}")
    {
    }
}
