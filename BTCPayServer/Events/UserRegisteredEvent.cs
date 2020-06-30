using System;
using BTCPayServer.Data;

namespace BTCPayServer.Events
{
    public class UserRegisteredEvent
    {
        public ApplicationUser User { get; set; }
        public bool Admin { get; set; }
        public Uri RequestUri { get; set; }
    }
}
