#if DEBUG
using System.Data;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class JunkNotification: BaseNotification
    {
        private const string TYPE = "junk";
        internal class Handler : NotificationHandler<JunkNotification>
        {
            public override string NotificationType => TYPE;
            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] {(TYPE, "Junk")};
                }
            }

            protected override void FillViewModel(JunkNotification notification, NotificationViewModel vm)
            {
                vm.Body = $"All your junk r belong to us!";
            }
        }

        public override string Identifier => NotificationType;
        public override string NotificationType => TYPE;
    }
}
#endif
