namespace BTCPayServer.Services.Custodian.Client.Exception;

public class InsufficientFundsException : CustodianApiException
{
    
    public const int HttpCode = 400;
    public System.Exception OriginalException { get; }

    public InsufficientFundsException(string message) : base(HttpCode, "insufficient-funds", message)
    {
    }

    public InsufficientFundsException(System.Exception e) : base(HttpCode, "insufficient-funds", $"Insufficient funds: {e.Message}")
    {
        this.OriginalException = e;
    }
}
