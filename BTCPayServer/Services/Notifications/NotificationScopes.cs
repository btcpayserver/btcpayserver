using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Notifications
{
    public class AdminScope : NotificationScope
    {
        public AdminScope()
        {
        }
    }
    public class StoreScope : NotificationScope
    {
        public StoreScope(string storeId)
        {
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            StoreId = storeId;
        }
        public string StoreId { get; }
    }

    public interface NotificationScope
    {
    }
}
