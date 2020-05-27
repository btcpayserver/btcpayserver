using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data.Data;
using ExchangeSharp;
using Newtonsoft.Json;

namespace BTCPayServer.Events
{
    public abstract class NotificationEventBase
    {
        public NotificationData ToData()
        {
            var obj = JsonConvert.SerializeObject(this);

            var data = new NotificationData
            {
                Created = DateTimeOffset.UtcNow,
                NotificationType = GetType().Name,
                Blob = obj.ToBytesUTF8(),
                Seen = false
            };
            return data;
        }
    }
}
