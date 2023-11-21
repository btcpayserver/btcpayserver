using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PayoutExtensions
    {
        public static async Task<PayoutData> GetPayout(this DbSet<PayoutData> payouts, string payoutId, string storeId, bool includePullPayment = false, bool includeStore = false)
        {
            IQueryable<PayoutData> query = payouts;
            if (includePullPayment)
                query = query.Include(p => p.PullPaymentData);
            if (includeStore)
                query = query.Include(p => p.StoreData);
            var payout = await query.Where(p => p.Id == payoutId &&
                                                p.StoreDataId == storeId).FirstOrDefaultAsync();
            if (payout is null)
                return null;
            return payout;
        }

        public static PaymentMethodId GetPaymentMethodId(this PayoutData data)
        {
            return PaymentMethodId.TryParse(data.PaymentMethodId, out var paymentMethodId) ? paymentMethodId : null;
        }

        public static string GetPayoutSource(this PayoutData data, BTCPayNetworkJsonSerializerSettings jsonSerializerSettings)
        {
            var ppBlob = data.PullPaymentData?.GetBlob();
            var payoutBlob = data.GetBlob(jsonSerializerSettings);
            return payoutBlob.Metadata?.TryGetValue("source", StringComparison.InvariantCultureIgnoreCase, out var source) is true
                ? source.Value<string>()
                : ppBlob?.Name ?? data.PullPaymentDataId;
        }

        public static PayoutBlob GetBlob(this PayoutData data, BTCPayNetworkJsonSerializerSettings serializers)
        {
            var result =  JsonConvert.DeserializeObject<PayoutBlob>(Encoding.UTF8.GetString(data.Blob), serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode));
            result.Metadata ??= new JObject();
            return result;
        }
        public static void SetBlob(this PayoutData data, PayoutBlob blob, BTCPayNetworkJsonSerializerSettings serializers)
        {
            data.Blob = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob, serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode)));
        }

        public static JObject GetProofBlobJson(this PayoutData data)
        {
            return data?.Proof is null ? null : JObject.Parse(Encoding.UTF8.GetString(data.Proof));
        }
        public static void SetProofBlob(this PayoutData data, IPayoutProof blob, JsonSerializerSettings settings)
        {
            if (blob is null)
            {
                data.Proof = null;
                return;
            }

            data.SetProofBlob(settings is null
                ? JObject.FromObject(blob)
                : JObject.FromObject(blob, JsonSerializer.Create(settings)));
        }
        public static void SetProofBlob(this PayoutData data, JObject blob)
        {
            if (blob is null)
            {
                data.Proof = null;
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(blob.ToString(Formatting.None));
            // We only update the property if the bytes actually changed, this prevent from hammering the DB too much
            if (data.Proof is null || bytes.Length != data.Proof.Length || !bytes.SequenceEqual(data.Proof))
            {
                data.Proof = bytes;
            }
        }

        public static async Task<List<PaymentMethodId>> GetSupportedPaymentMethods(
            this IEnumerable<IPayoutHandler> payoutHandlers, StoreData storeData)
        {
            return (await Task.WhenAll(payoutHandlers.Select(handler => handler.GetSupportedPaymentMethods(storeData)))).SelectMany(ids => ids).ToList();
        }
    }
}
