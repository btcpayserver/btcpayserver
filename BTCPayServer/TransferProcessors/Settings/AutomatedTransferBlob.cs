using System;

namespace BTCPayServer.TransferProcessors.Settings;

public class AutomatedTransferBlob
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}
