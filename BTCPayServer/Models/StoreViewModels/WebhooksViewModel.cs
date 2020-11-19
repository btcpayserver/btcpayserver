using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class WebhooksViewModel
    {
        public class WebhookViewModel
        {
            public string Id { get; set; }
            public string Url { get; set; }
        }
        public WebhookViewModel[] Webhooks { get; set; }
    }
}
