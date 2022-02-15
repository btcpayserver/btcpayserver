namespace BTCPayServer.Services.Custodian.Client.Exception;

public class InvalidWithdrawalTargetException : CustodianApiException

{
    public InvalidWithdrawalTargetException(ICustodian custodian, string asset, string targetAddress, CustodianApiException originalException) : base(403, "invalid-withdrawal-target", $"{custodian.GetName()} cannot withdraw {asset} to '{targetAddress}': {originalException.Message}")
    {
    }
}
