using System;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Events
{
    public class UserRegisteredEvent
    {
        public ApplicationUser User { get; set; }
        public bool Admin { get; set; }
        public Uri RequestUri { get; set; }
    }
}
