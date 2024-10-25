using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class UpdateNotificationSettingsRequest
{
    public List<string> Disabled { get; set; }
}
