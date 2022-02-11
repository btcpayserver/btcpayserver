using System;

namespace BTCPayServer.Services.Custodian.Client.Exception;

public class CustodianApiException: SystemException

{
    public int HttpStatus { get; }
    public string Code { get; }
    
    public CustodianApiException( int httpStatus, string code, string message) : base(message)
    {
        this.HttpStatus = httpStatus;
        this.Code = code;
    }
}

