namespace BTCPayServer.Client.Models;

public class OfferingModel
{
    public string Id { get; set; } = null!;
    public string AppName { get; set; }
    public string AppId { get; set; } = null!;
    public string SuccessRedirectUrl { get; set; }

}
