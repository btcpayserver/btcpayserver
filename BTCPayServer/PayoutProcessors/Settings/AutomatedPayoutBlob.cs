using System;

namespace BTCPayServer.PayoutProcessors.Settings;

public class AutomatedPayoutBlob
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}
