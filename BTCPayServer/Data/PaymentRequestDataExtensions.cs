using System;
using BTCPayServer.Client.Models;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentRequestDataExtensions
    {
        public static readonly JsonSerializerSettings DefaultSerializerSettings;
        public static readonly JsonSerializer DefaultSerializer;
        static PaymentRequestDataExtensions()
        {
            (DefaultSerializerSettings, DefaultSerializer) = BlobSerializer.CreateSerializer(null as NBitcoin.Network);
        }
        public static PaymentRequestBlob GetBlob(this PaymentRequestData paymentRequestData)
        {
            return paymentRequestData.HasTypedBlob<PaymentRequestBlob>().GetBlob(DefaultSerializerSettings) ?? new PaymentRequestBlob();
        }

        public static void SetBlob(this PaymentRequestData paymentRequestData, PaymentRequestBlob blob)
        {
            paymentRequestData.HasTypedBlob<PaymentRequestBlob>().SetBlob(blob, DefaultSerializer);
        }
    }
}
