using System;

namespace BTCPayServer.Abstractions.Custodians.Client.Exception;

public class BadConfigException : CustodianApiException
{
    public string[] BadConfigKeys { get; set; }

    public BadConfigException(string[] badConfigKeys) : base(500, "bad-custodian-account-config", "Wrong config values: " + String.Join(", ", badConfigKeys))
    {
        this.BadConfigKeys = badConfigKeys;
    }
}
