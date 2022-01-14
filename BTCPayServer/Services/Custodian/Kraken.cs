namespace BTCPayServer.Services.Custodian;

public class Kraken: ICustodian
{
    public string getCode()
    {
        return "kraken";
    }

    public string getName()
    {
        return "Kraken";
    }

    public string? getHomepage()
    {
        return "https://www.kraken.com";
    }

    public string? getDescription()
    {
        return null;
    }
}
