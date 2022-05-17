namespace BTCPayServer.Abstractions.Custodians;

public class InvalidWithdrawalTargetException : CustodianApiException

{
    public InvalidWithdrawalTargetException(ICustodian custodian, string paymentMethod, string targetAddress, CustodianApiException originalException) : base(403, "invalid-withdrawal-target", $"{custodian.GetName()} cannot withdraw {paymentMethod} to '{targetAddress}': {originalException.Message}")
    {
    }
}
