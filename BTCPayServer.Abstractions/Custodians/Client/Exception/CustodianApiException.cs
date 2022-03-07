using System;

namespace BTCPayServer.Abstractions.Custodians.Client.Exception;

public class CustodianApiException: SystemException {
    public int HttpStatus { get; }
    public string Code { get; }

    public CustodianApiException( int httpStatus, string code, string message) : base(message)
    {
        HttpStatus = httpStatus;
        Code = code;
    }
}

