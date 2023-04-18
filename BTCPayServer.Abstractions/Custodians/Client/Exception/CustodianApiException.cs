using System;
namespace BTCPayServer.Abstractions.Custodians;
public class CustodianApiException : Exception
{
    public int HttpStatus { get; }
    public string Code { get; }

    public CustodianApiException(int httpStatus, string code, string message, System.Exception ex) : base(message, ex)
    {
        HttpStatus = httpStatus;
        Code = code;
    }

    public CustodianApiException(int httpStatus, string code, string message) : this(httpStatus, code, message, null)
    {
    }
}

