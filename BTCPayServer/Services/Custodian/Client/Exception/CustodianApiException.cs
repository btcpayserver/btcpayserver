using System;

namespace BTCPayServer.Services.Custodian.Client.Exception;

public class CustodianApiException: SystemException

{
    public CustodianApiException(string message) : base(message)
    {
    }
}
