namespace BTCPayServer.Services.Custodian.Client;

public class CustodianData
{
    public string code;
    public string name;

    public CustodianData(ICustodian custodian)
    {
        code = custodian.getCode();
        name = custodian.getName();
    }
}
