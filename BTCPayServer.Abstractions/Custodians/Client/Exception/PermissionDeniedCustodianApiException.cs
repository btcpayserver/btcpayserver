namespace BTCPayServer.Abstractions.Custodians;

public class PermissionDeniedCustodianApiException : CustodianApiException

{
    public PermissionDeniedCustodianApiException(ICustodian custodian) : base(403, "custodian-api-permission-denied", $"{custodian.Name}'s API reported that you don't have permission.")
    {
    }
}
