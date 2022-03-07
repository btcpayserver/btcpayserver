namespace BTCPayServer.Abstractions.Custodians.Client.Exception;

public class InsufficientFundsException : CustodianApiException
{

    public const int HttpStatus = 400;
    public const string ErrorCode = "insufficient-funds";
    
    public InsufficientFundsException(string message) : base(HttpStatus, ErrorCode, message)
    {
    }

}
