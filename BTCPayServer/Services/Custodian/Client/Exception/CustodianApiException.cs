using System;

namespace BTCPayServer.Services.Custodian.Client.Exception;

public class KrakenApiException: SystemException

{
    public KrakenApiException(string message) : base(message)
    {
    }
}
