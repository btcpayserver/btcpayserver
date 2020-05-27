using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Events.Notifications
{
    public static class NotificationsHelperExt
    {
        public static void NoticeNewVersion(this EventAggregator aggr, string version)
        {
            aggr.Publish(new NewVersionNotification
            {
                Version = version
            });
        }
    }
}
