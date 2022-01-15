namespace BTCPayServer.Services.Custodian;

public class Kraken: ICustodian
{

    private static Kraken _instance;
    
    private Kraken()
    {
        
    }

    public static Kraken getInstance()
    {
        if (_instance == null)
        {
            _instance = new Kraken();
        }

        return _instance;
    }
    
    public string getCode()
    {
        return "kraken";
    }

    public string getName()
    {
        return "Kraken";
    }

}
