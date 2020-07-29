using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.JsonConverters;
using NBitcoin.Payment;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class PullPaymentsExtensions
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
        public static PullPaymentBlob GetBlob(this PullPaymentData data)
        {
            return JsonConvert.DeserializeObject<PullPaymentBlob>(Encoding.UTF8.GetString(data.Blob));
        }
        public static void SetBlob(this PullPaymentData data, PullPaymentBlob blob)
        {
            data.Blob = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob));
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

        public static bool IsSupported(this PullPaymentData data, BTCPayServer.Payments.PaymentMethodId paymentId)
        {
            return data.GetBlob().SupportedPaymentMethods.Contains(paymentId);
        }

        public static PayoutTransactionOnChainBlob GetProofBlob(this PayoutData data, BTCPayNetworkJsonSerializerSettings serializers)
        {
            if (data.Proof is null)
                return null;
            return JsonConvert.DeserializeObject<PayoutTransactionOnChainBlob>(Encoding.UTF8.GetString(data.Proof), serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode));
        }
        public static void SetProofBlob(this PayoutData data, PayoutTransactionOnChainBlob blob, BTCPayNetworkJsonSerializerSettings serializers)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob, serializers.GetSerializer(data.GetPaymentMethodId().CryptoCode)));
            // We only update the property if the bytes actually changed, this prevent from hammering the DB too much
            if (data.Proof is null || bytes.Length != data.Proof.Length || !bytes.SequenceEqual(data.Proof))
            {
                data.Proof = bytes;
            }
        }
    }

    public class PayoutTransactionOnChainBlob
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
        [JsonProperty(ItemConverterType = typeof(NBitcoin.JsonConverters.UInt256JsonConverter), NullValueHandling = NullValueHandling.Ignore)]
        public HashSet<uint256> Candidates { get; set; } = new HashSet<uint256>();
    }
    public interface IClaimDestination
    {
        BitcoinAddress Address { get; }
    }
    public static class ClaimDestination
    {
        public static bool TryParse(string destination, BTCPayNetwork network, out IClaimDestination claimDestination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination = destination.Trim();
            try
            {
                if (destination.StartsWith($"{network.UriScheme}:", StringComparison.OrdinalIgnoreCase))
                {
                    claimDestination = new UriClaimDestination(new BitcoinUrlBuilder(destination, network.NBitcoinNetwork));
                }
                else
                {
                    claimDestination = new AddressClaimDestination(BitcoinAddress.Create(destination, network.NBitcoinNetwork));
                }
                return true;
            }
            catch
            {
                claimDestination = null;
                return false;
            }
        }
    }
    public class AddressClaimDestination : IClaimDestination
    {
        private readonly BitcoinAddress _bitcoinAddress;

        public AddressClaimDestination(BitcoinAddress bitcoinAddress)
        {
            if (bitcoinAddress == null)
                throw new ArgumentNullException(nameof(bitcoinAddress));
            _bitcoinAddress = bitcoinAddress;
        }
        public BitcoinAddress BitcoinAdress => _bitcoinAddress;
        public BitcoinAddress Address => _bitcoinAddress;
        public override string ToString()
        {
            return _bitcoinAddress.ToString();
        }
    }
    public class UriClaimDestination : IClaimDestination
    {
        private readonly BitcoinUrlBuilder _bitcoinUrl;

        public UriClaimDestination(BitcoinUrlBuilder bitcoinUrl)
        {
            if (bitcoinUrl == null)
                throw new ArgumentNullException(nameof(bitcoinUrl));
            if (bitcoinUrl.Address is null)
                throw new ArgumentException(nameof(bitcoinUrl));
            _bitcoinUrl = bitcoinUrl;
        }
        public BitcoinUrlBuilder BitcoinUrl => _bitcoinUrl;

        public BitcoinAddress Address => _bitcoinUrl.Address;
        public override string ToString()
        {
            return _bitcoinUrl.ToString();
        }
    }
    public class PayoutBlob
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? CryptoAmount { get; set; }
        public int MinimumConfirmation { get; set; } = 1;
        public IClaimDestination Destination { get; set; }
        public int Revision { get; set; }
    }
    public class ClaimDestinationJsonConverter : JsonConverter<IClaimDestination>
    {
        private readonly BTCPayNetwork _network;

        public ClaimDestinationJsonConverter(BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _network = network;
        }

        public override IClaimDestination ReadJson(JsonReader reader, Type objectType, IClaimDestination existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("Expected string for IClaimDestination", reader);
            if (ClaimDestination.TryParse((string)reader.Value, _network, out var v))
                return v;
            throw new JsonObjectException("Invalid IClaimDestination", reader);
        }

        public override void WriteJson(JsonWriter writer, IClaimDestination value, JsonSerializer serializer)
        {
            if (value is IClaimDestination v)
                writer.WriteValue(v.ToString());
        }
    }
    public class PullPaymentBlob
    {
        public string Name { get; set; }
        public string Currency { get; set; }
        public int Divisibility { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Limit { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal MinimumClaim { get; set; }
        public PullPaymentView View { get; set; } = new PullPaymentView();
        [JsonConverter(typeof(TimeSpanJsonConverter))]
        public TimeSpan? Period { get; set; }

        [JsonProperty(ItemConverterType = typeof(PaymentMethodIdJsonConverter))]
        public PaymentMethodId[] SupportedPaymentMethods { get; set; }
    }
    public class PullPaymentView
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
        public string Email { get; set; }
        public string CustomCSSLink { get; set; }
    }
}
