using System;
using BTCPayServer.Data;

namespace BTCPayServer.Events
{
    public class UserApprovedEvent
    {
        public ApplicationUser User { get; set; }
        public bool Approved { get; set; }
        public Uri RequestUri { get; set; }
    }
}
