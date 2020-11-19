using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public enum WebhookDeliveryStatus
    {
        Failed,
        HttpError,
        HttpSuccess
    }
}
