using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class NotificationSettingsData
{
    public List<NotificationSettingsItemData> Notifications { get; set; }
}

public class NotificationSettingsItemData
{
    public string Identifier { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
}
