namespace BTCPayServer.Services.Custodian.Client.Exception;

public class InsufficientFundsException : CustodianApiException
{

    public InsufficientFundsException(string message) : base(400, "insufficient-funds", message)
    {
    }

}
