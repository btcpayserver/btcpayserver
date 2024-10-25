using System;
using BTCPayServer.Client.Models;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentRequestDataExtensions
    {
        public static PaymentRequestBaseData GetBlob(this PaymentRequestData paymentRequestData)
        {
            return paymentRequestData.HasTypedBlob<PaymentRequestBaseData>().GetBlob() ?? new PaymentRequestBaseData();
        }

        public static void SetBlob(this PaymentRequestData paymentRequestData, PaymentRequestBaseData blob)
        {
            paymentRequestData.HasTypedBlob<PaymentRequestBaseData>().SetBlob(blob);
        }
    }
}
