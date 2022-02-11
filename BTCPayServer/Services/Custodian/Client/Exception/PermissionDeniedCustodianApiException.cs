using System;

namespace BTCPayServer.Services.Custodian.Client.Exception;

public class PermissionDeniedCustodianApiException : CustodianApiException

{
    public PermissionDeniedCustodianApiException(ICustodian custodian) : base(403, "custodian-api-permission-denied", $"{custodian.GetName()}'s API reported that you don't have permission.")
    {
    }
}
