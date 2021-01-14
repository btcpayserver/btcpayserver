using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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
                query = query.Include(p => p.PullPaymentData.StoreData);
            var payout = await query.Where(p => p.Id == payoutId &&
                                                p.PullPaymentData.StoreId == storeId).FirstOrDefaultAsync();
            if (payout is null)
                return null;
            return payout;
        }
        
        public static PaymentMethodId GetPaymentMethodId(this PayoutData data)
        {
            return PaymentMethodId.Parse(data.PaymentMethodId);
        }
        public static PayoutBlob GetBlob(this PayoutData data, BTCPayNetworkJsonSerializerSettings serializers)
        {
            return JsonConvert.DeserializeObject<PayoutBlob>(Encoding.UTF8.GetString(data.Blob), serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode));
        }
        public static void SetBlob(this PayoutData data, PayoutBlob blob, BTCPayNetworkJsonSerializerSettings serializers)
        {
            data.Blob = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob, serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode)));
        }
    }
}
