namespace BTCPayServer.Abstractions.Custodians;

public class CustodianFeatureNotImplementedException : CustodianApiException
{
    public CustodianFeatureNotImplementedException(string message) : base(400, "not-implemented", message)
    {
    }
}
