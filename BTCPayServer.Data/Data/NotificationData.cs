using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Data.Data
{
    public class NotificationData
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string ApplicationUserId { get; set; }
        public string NotificationType { get; set; }
        public bool Seen { get; set; }
        public byte[] Blob { get; set; }
    }
}
