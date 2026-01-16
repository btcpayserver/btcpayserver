namespace BTCPayServer.Plugins.Webhooks.Views;

/// <summary>
/// Used to represent available webhooks with their descriptions in the ModifyWebhook view
/// </summary>
/// <param name="type">The webhook type</param>
/// <param name="description">User friendly description</param>
public class AvailableWebhookViewModel(string type, string description)
{
    public string Type { get; } = type;
    public string Description { get; } = description;
}
