using System;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Events
{
    public class UserPasswordResetRequestedEvent
    {
        public ApplicationUser User { get; set; }
        public Uri RequestUri { get; set; }
        public TaskCompletionSource<Uri> CallbackUrlGenerated;
    }
}
