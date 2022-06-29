namespace BTCPayServer.Client.Models;

public class EmailSettingsData
{
    public string Server
    {
        get; set;
    }

    public int? Port
    {
        get; set;
    }

    public string Login
    {
        get; set;
    }

    public string Password
    {
        get; set;
    }
    public string From
    {
        get; set;
    }
    public bool DisableCertificateCheck { get; set; }
}
