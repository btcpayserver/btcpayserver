using System;
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserConfirmedEmailEvent
{
    public ApplicationUser User { get; set; }
    public Uri RequestUri { get; set; }
}
