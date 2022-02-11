namespace BTCPayServer.Services.Custodian.Client.Exception;

public class DepositsUnavailableException : CustodianApiException
{
    public DepositsUnavailableException(string message) : base(404, "deposits-unavailable", message)
    {
    }
}
