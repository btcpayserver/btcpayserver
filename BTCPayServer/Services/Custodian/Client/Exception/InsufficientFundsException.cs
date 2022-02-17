namespace BTCPayServer.Services.Custodian.Client.Exception;

public class InsufficientFundsException : CustodianApiException
{
    public System.Exception OriginalException { get; }

    public InsufficientFundsException(string message) : base(403, "insufficient-funds", message)
    {
    }

    public InsufficientFundsException(System.Exception e) : base(403, "insufficient-funds", $"Insufficient funds: {e.Message}")
    {
        this.OriginalException = e;
    }
}
