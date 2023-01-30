using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.JsonConverters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceCryptoInfo : NBitpayClient.InvoiceCryptoInfo
    {
        [JsonProperty("paymentUrls")]
        public new InvoicePaymentUrls PaymentUrls { get; set; }
        public class InvoicePaymentUrls : NBitpayClient.InvoicePaymentUrls
        {
            [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
        }
    }
    public class InvoiceMetadata
    {
        public static readonly JsonSerializer MetadataSerializer;
        static InvoiceMetadata()
        {
            var seria = new JsonSerializer();
            seria.DefaultValueHandling = DefaultValueHandling.Ignore;
            seria.FloatParseHandling = FloatParseHandling.Decimal;
            seria.ContractResolver = new CamelCasePropertyNamesContractResolver();
            MetadataSerializer = seria;
        }

        [JsonIgnore]
        public string OrderId
        {
            get => GetMetadata<string>("orderId");
            set => SetMetadata("orderId", value);
        }
        [JsonIgnore]
        public string OrderUrl
        {
            get => GetMetadata<string>("orderUrl");
            set => SetMetadata("orderUrl", value);
        }
        [JsonIgnore]
        public string PaymentRequestId
        {
            get => GetMetadata<string>("paymentRequestId");
            set => SetMetadata("paymentRequestId", value);
        }
        [JsonIgnore]
        public string BuyerName
        {
            get => GetMetadata<string>("buyerName");
            set => SetMetadata("buyerName", value);
        }
        [JsonIgnore]
        public string BuyerEmail
        {
            get => GetMetadata<string>("buyerEmail");
            set => SetMetadata("buyerEmail", value);
        }
        [JsonIgnore]
        public string BuyerCountry
        {
            get => GetMetadata<string>("buyerCountry");
            set => SetMetadata("buyerCountry", value);
        }
        [JsonIgnore]
        public string BuyerZip
        {
            get => GetMetadata<string>("buyerZip");
            set => SetMetadata("buyerZip", value);
        }
        [JsonIgnore]
        public string BuyerState
        {
            get => GetMetadata<string>("buyerState");
            set => SetMetadata("buyerState", value);
        }
        [JsonIgnore]
        public string BuyerCity
        {
            get => GetMetadata<string>("buyerCity");
            set => SetMetadata("buyerCity", value);
        }
        [JsonIgnore]
        public string BuyerAddress2
        {
            get => GetMetadata<string>("buyerAddress2");
            set => SetMetadata("buyerAddress2", value);
        }
        [JsonIgnore]
        public string BuyerAddress1
        {
            get => GetMetadata<string>("buyerAddress1");
            set => SetMetadata("buyerAddress1", value);
        }
        [JsonIgnore]
        public string BuyerPhone
        {
            get => GetMetadata<string>("buyerPhone");
            set => SetMetadata("buyerPhone", value);
        }
        [JsonIgnore]
        public string ItemDesc
        {
            get => GetMetadata<string>("itemDesc");
            set => SetMetadata("itemDesc", value);
        }
        [JsonIgnore]
        public string ItemCode
        {
            get => GetMetadata<string>("itemCode");
            set => SetMetadata("itemCode", value);
        }
        [JsonIgnore]
        public bool? Physical
        {
            get => GetMetadata<bool?>("physical");
            set => SetMetadata("physical", value);
        }
        [JsonIgnore]
        public decimal? TaxIncluded
        {
            get => GetMetadata<decimal?>("taxIncluded");
            set => SetMetadata("taxIncluded", value);
        }
        [JsonIgnore]
        public string PosData
        {
            get => GetMetadata<string>("posData");
            set => SetMetadata("posData", value);
        }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }

        public T GetMetadata<T>(string propName)
        {
            if (AdditionalData == null || !(AdditionalData.TryGetValue(propName, out var jt) is true))
                return default;
            if (jt.Type == JTokenType.Null)
                return default;
            if (typeof(T) == typeof(string))
            {
                return (T)(object)jt.ToString();
            }

            try
            {
                return jt.Value<T>();
            }
            catch (Exception)
            {
                return default;
            }
        }
        public void SetMetadata<T>(string propName, T value)
        {
            JToken data;
            if (typeof(T) == typeof(string) && value is string v)
            {
                data = new JValue(v);
                AdditionalData ??= new Dictionary<string, JToken>();
                AdditionalData.AddOrReplace(propName, data);
                return;
            }
            if (value is null)
            {
                AdditionalData?.Remove(propName);
            }
            else
            {
                try
                {
                    if (value is string s)
                    {
                        data = JToken.Parse(s);
                    }
                    else
                    {
                        data = JToken.FromObject(value);
                    }
                }
                catch (Exception)
                {
                    data = JToken.FromObject(value);
                }

                AdditionalData ??= new Dictionary<string, JToken>();
                AdditionalData.AddOrReplace(propName, data);
            }
        }

        public static InvoiceMetadata FromJObject(JObject jObject)
        {
            return jObject.ToObject<InvoiceMetadata>(MetadataSerializer);
        }
        public JObject ToJObject()
        {
            return JObject.FromObject(this, MetadataSerializer);
        }
    }

    public class InvoiceEntity
    {
        class BuyerInformation
        {
            [JsonProperty(PropertyName = "buyerName")]
            public string BuyerName { get; set; }
            [JsonProperty(PropertyName = "buyerEmail")]
            public string BuyerEmail { get; set; }
            [JsonProperty(PropertyName = "buyerCountry")]
            public string BuyerCountry { get; set; }
            [JsonProperty(PropertyName = "buyerZip")]
            public string BuyerZip { get; set; }
            [JsonProperty(PropertyName = "buyerState")]
            public string BuyerState { get; set; }
            [JsonProperty(PropertyName = "buyerCity")]
            public string BuyerCity { get; set; }
            [JsonProperty(PropertyName = "buyerAddress2")]
            public string BuyerAddress2 { get; set; }
            [JsonProperty(PropertyName = "buyerAddress1")]
            public string BuyerAddress1 { get; set; }

            [JsonProperty(PropertyName = "buyerPhone")]
            public string BuyerPhone { get; set; }
        }

        class ProductInformation
        {
            [JsonProperty(PropertyName = "itemDesc")]
            public string ItemDesc { get; set; }
            [JsonProperty(PropertyName = "itemCode")]
            public string ItemCode { get; set; }
            [JsonProperty(PropertyName = "physical")]
            public bool Physical { get; set; }

            [JsonProperty(PropertyName = "price")]
            public decimal Price { get; set; }

            [JsonProperty(PropertyName = "taxIncluded", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public decimal TaxIncluded { get; set; }

            [JsonProperty(PropertyName = "currency")]
            public string Currency { get; set; }
        }

        [JsonIgnore]
        public BTCPayNetworkProvider Networks { get; set; }
        public const int InternalTagSupport_Version = 1;
        public const int GreenfieldInvoices_Version = 2;
        public const int Lastest_Version = 2;
        public int Version { get; set; }
        public string Id { get; set; }
        public string StoreId { get; set; }

        public SpeedPolicy SpeedPolicy { get; set; }
        public string DefaultLanguage { get; set; }
        [Obsolete("Use GetPaymentMethod(network) instead")]
        public decimal Rate { get; set; }
        public DateTimeOffset InvoiceTime { get; set; }
        public DateTimeOffset ExpirationTime { get; set; }

        [Obsolete("Use GetPaymentMethod(network).GetPaymentMethodDetails().GetDestinationAddress() instead")]
        public string DepositAddress { get; set; }

        public InvoiceMetadata Metadata { get; set; }

        public decimal Price { get; set; }
        public string Currency { get; set; }
        public string DefaultPaymentMethod { get; set; }
#nullable enable
        public PaymentMethodId? GetDefaultPaymentMethod()
        {
            PaymentMethodId.TryParse(DefaultPaymentMethod, out var id);
            return id;
        }
#nullable restore
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HashSet<string> InternalTags { get; set; } = new HashSet<string>();

        public string[] GetInternalTags(string prefix)
        {
            return InternalTags == null ? Array.Empty<string>() : InternalTags
                                                  .Where(t => t.StartsWith(prefix, StringComparison.InvariantCulture))
                                                  .Select(t => t.Substring(prefix.Length)).ToArray();
        }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategy { get; set; }

        [Obsolete("Use GetPaymentMethodFactories() instead")]
        public string DerivationStrategies { get; set; }
        public IEnumerable<T> GetSupportedPaymentMethod<T>(PaymentMethodId paymentMethodId) where T : ISupportedPaymentMethod
        {
            return
                GetSupportedPaymentMethod()
                .Where(p => paymentMethodId == null || p.PaymentId == paymentMethodId)
                .OfType<T>();
        }
        public IEnumerable<T> GetSupportedPaymentMethod<T>() where T : ISupportedPaymentMethod
        {
            return GetSupportedPaymentMethod<T>(null);
        }
        public IEnumerable<ISupportedPaymentMethod> GetSupportedPaymentMethod()
        {
#pragma warning disable CS0618
            bool btcReturned = false;
            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    if (!PaymentMethodId.TryParse(strat.Name, out var paymentMethodId))
                    {
                        continue;
                    }
                    var network = Networks.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
                    if (network != null)
                    {
                        if (network == Networks.BTC && paymentMethodId.PaymentType == PaymentTypes.BTCLike)
                            btcReturned = true;
                        yield return paymentMethodId.PaymentType.DeserializeSupportedPaymentMethod(network, strat.Value);
                    }
                }
            }

            if (!btcReturned && !string.IsNullOrEmpty(DerivationStrategy))
            {
                if (Networks.BTC != null)
                {
                    yield return BTCPayServer.DerivationSchemeSettings.Parse(DerivationStrategy, Networks.BTC);
                }
            }
#pragma warning restore CS0618
        }

        internal void SetSupportedPaymentMethods(IEnumerable<ISupportedPaymentMethod> derivationStrategies)
        {
            JObject obj = new JObject();
            foreach (var strat in derivationStrategies)
            {
                obj.Add(strat.PaymentId.ToString(), PaymentMethodExtensions.Serialize(strat));
#pragma warning disable CS0618
                // This field should eventually disappear
                DerivationStrategy = null;
            }
            DerivationStrategies = JsonConvert.SerializeObject(obj);
#pragma warning restore CS0618
        }

        [JsonIgnore]
        public InvoiceStatusLegacy Status { get; set; }
        [JsonProperty(PropertyName = "status")]
        [Obsolete("Use Status instead")]
        public string StatusString => InvoiceState.ToString(Status);
        [JsonIgnore]
        public InvoiceExceptionStatus ExceptionStatus { get; set; }
        [JsonProperty(PropertyName = "exceptionStatus")]
        [Obsolete("Use ExceptionStatus instead")]
        public string ExceptionStatusString => InvoiceState.ToString(ExceptionStatus);

        [Obsolete("Use GetPayments instead")]
        public List<PaymentEntity> Payments { get; set; }

#pragma warning disable CS0618
        public List<PaymentEntity> GetPayments(bool accountedOnly)
        {
            return Payments?.Where(entity => entity.GetPaymentMethodId() != null && (!accountedOnly || entity.Accounted)).ToList() ?? new List<PaymentEntity>();
        }
        public List<PaymentEntity> GetPayments(string cryptoCode, bool accountedOnly)
        {
            return GetPayments(accountedOnly).Where(p => p.CryptoCode == cryptoCode).ToList();
        }
        public List<PaymentEntity> GetPayments(BTCPayNetworkBase network, bool accountedOnly)
        {
            return GetPayments(network.CryptoCode, accountedOnly);
        }
#pragma warning restore CS0618
        // public bool Refundable { get; set; }
        public bool? RequiresRefundEmail { get; set; } = null;
        public string RefundMail { get; set; }
        [JsonProperty("redirectURL")]
        public string RedirectURLTemplate { get; set; }

        [JsonIgnore]
        public Uri RedirectURL => FillPlaceholdersUri(RedirectURLTemplate);

        private Uri FillPlaceholdersUri(string v)
        {
            var uriStr = (v ?? string.Empty).Replace("{OrderId}", System.Web.HttpUtility.UrlEncode(Metadata.OrderId) ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{InvoiceId}", System.Web.HttpUtility.UrlEncode(Id) ?? "", StringComparison.OrdinalIgnoreCase);
            if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                return uri;
            return null;
        }

        public bool RedirectAutomatically { get; set; }

        [Obsolete("Use GetPaymentMethod(network).GetTxFee() instead")]
        public Money TxFee { get; set; }
        public bool FullNotifications { get; set; }
        public string NotificationEmail { get; set; }

        [JsonProperty("notificationURL")]
        public string NotificationURLTemplate { get; set; }

        [JsonIgnore]
        public Uri NotificationURL => FillPlaceholdersUri(NotificationURLTemplate);
        public string ServerUrl { get; set; }

        [Obsolete("Use Set/GetPaymentMethod() instead")]
        [JsonProperty(PropertyName = "cryptoData")]
        public JObject PaymentMethod { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public DateTimeOffset MonitoringExpiration { get; set; }

        public HashSet<string> AvailableAddressHashes { get; set; }
        public bool ExtendedNotifications { get; set; }
        public List<InvoiceEventData> Events { get; internal set; }
        public double PaymentTolerance { get; set; }
        public bool Archived { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public InvoiceType Type { get; set; }

        public List<RefundData> Refunds { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public InvoiceDataBase.ReceiptOptions ReceiptOptions { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public CheckoutType? CheckoutType { get; set; }

        public bool IsExpired()
        {
            return DateTimeOffset.UtcNow > ExpirationTime;
        }

        public InvoiceResponse EntityToDTO()
        {
            ServerUrl = ServerUrl ?? "";
            InvoiceResponse dto = new InvoiceResponse
            {
                Id = Id,
                StoreId = StoreId,
                OrderId = Metadata.OrderId,
                PosData = Metadata.PosData,
                CurrentTime = DateTimeOffset.UtcNow,
                InvoiceTime = InvoiceTime,
                ExpirationTime = ExpirationTime,
#pragma warning disable CS0618 // Type or member is obsolete
                Status = StatusString,
                ExceptionStatus = ExceptionStatus == InvoiceExceptionStatus.None ? new JValue(false) : new JValue(ExceptionStatusString),
#pragma warning restore CS0618 // Type or member is obsolete
                Currency = Currency,
                PaymentSubtotals = new Dictionary<string, decimal>(),
                PaymentTotals = new Dictionary<string, decimal>(),
                SupportedTransactionCurrencies = new Dictionary<string, NBitpayClient.InvoiceSupportedTransactionCurrency>(),
                Addresses = new Dictionary<string, string>(),
                PaymentCodes = new Dictionary<string, InvoiceCryptoInfo.InvoicePaymentUrls>(),
                ExchangeRates = new Dictionary<string, Dictionary<string, decimal>>()
            };

            dto.Url = ServerUrl.WithTrailingSlash() + $"invoice?id=" + Id;
            dto.CryptoInfo = new List<InvoiceCryptoInfo>();
            dto.MinerFees = new Dictionary<string, MinerFeeInfo>();
            foreach (var info in this.GetPaymentMethods())
            {
                var accounting = info.Calculate();
                var cryptoInfo = new InvoiceCryptoInfo();
                var subtotalPrice = accounting.TotalDue - accounting.NetworkFee;
                var cryptoCode = info.GetId().CryptoCode;
                var details = info.GetPaymentMethodDetails();
                var address = details?.GetPaymentDestination();
                var exrates = new Dictionary<string, decimal>
                {
                    { Currency, cryptoInfo.Rate }
                };

                cryptoInfo.CryptoCode = cryptoCode;
                cryptoInfo.PaymentType = info.GetId().PaymentType.ToString();
                cryptoInfo.Rate = info.Rate;
                cryptoInfo.Price = subtotalPrice.ToString();

                cryptoInfo.Due = accounting.Due.ToString();
                cryptoInfo.Paid = accounting.Paid.ToString();
                cryptoInfo.TotalDue = accounting.TotalDue.ToString();
                cryptoInfo.NetworkFee = accounting.NetworkFee.ToString();
                cryptoInfo.TxCount = accounting.TxCount;
                cryptoInfo.CryptoPaid = accounting.CryptoPaid.ToString();

                cryptoInfo.Address = address;

                cryptoInfo.ExRates = exrates;
                var paymentId = info.GetId();
                cryptoInfo.Url = ServerUrl.WithTrailingSlash() + $"i/{paymentId}/{Id}";

                cryptoInfo.Payments = GetPayments(info.Network, true).Select(entity =>
                {
                    var data = entity.GetCryptoPaymentData();
                    return new InvoicePaymentInfo()
                    {
                        Id = data.GetPaymentId(),
                        Fee = entity.NetworkFee,
                        Value = data.GetValue(),
                        Completed = data.PaymentCompleted(entity),
                        Confirmed = data.PaymentConfirmed(entity, SpeedPolicy),
                        Destination = data.GetDestination(),
                        PaymentType = data.GetPaymentType().ToString(),
                        ReceivedDate = entity.ReceivedTime.DateTime
                    };
                }).ToList();


                if (details?.Activated is true)
                {

                    paymentId.PaymentType.PopulateCryptoInfo(info, cryptoInfo, ServerUrl);
                    if (paymentId.PaymentType == PaymentTypes.BTCLike)
                    {
                        var minerInfo = new MinerFeeInfo();
                        minerInfo.TotalFee = accounting.NetworkFee.Satoshi;
                        minerInfo.SatoshiPerBytes = ((BitcoinLikeOnChainPaymentMethod)details).FeeRate
                            .GetFee(1).Satoshi;
                        dto.MinerFees.TryAdd(cryptoInfo.CryptoCode, minerInfo);

#pragma warning disable 618
                        if (info.CryptoCode == "BTC")
                        {
                            dto.BTCPrice = cryptoInfo.Price;
                            dto.Rate = cryptoInfo.Rate;
                            dto.ExRates = cryptoInfo.ExRates;
                            dto.BitcoinAddress = cryptoInfo.Address;
                            dto.BTCPaid = cryptoInfo.Paid;
                            dto.BTCDue = cryptoInfo.Due;
                            dto.PaymentUrls = cryptoInfo.PaymentUrls;
                        }
#pragma warning restore 618
                    }
                }

                dto.CryptoInfo.Add(cryptoInfo);
                dto.PaymentCodes.Add(paymentId.ToString(), cryptoInfo.PaymentUrls);
                dto.PaymentSubtotals.Add(paymentId.ToString(), subtotalPrice.Satoshi);
                dto.PaymentTotals.Add(paymentId.ToString(), accounting.TotalDue.Satoshi);
                dto.SupportedTransactionCurrencies.TryAdd(cryptoCode, new InvoiceSupportedTransactionCurrency()
                {
                    Enabled = true
                });
                dto.Addresses.Add(paymentId.ToString(), address);
                dto.ExchangeRates.TryAdd(cryptoCode, exrates);
            }

            //dto.AmountPaid dto.MinerFees & dto.TransactionCurrency are not supported by btcpayserver as we have multi currency payment support per invoice

            dto.ItemCode = Metadata.ItemCode;
            dto.ItemDesc = Metadata.ItemDesc;
            dto.TaxIncluded = Metadata.TaxIncluded ?? 0m;
            dto.Price = Price;
            dto.Currency = Currency;
            dto.CheckoutType = CheckoutType;
            dto.Buyer = new JObject();
            dto.Buyer.Add(new JProperty("name", Metadata.BuyerName));
            dto.Buyer.Add(new JProperty("address1", Metadata.BuyerAddress1));
            dto.Buyer.Add(new JProperty("address2", Metadata.BuyerAddress2));
            dto.Buyer.Add(new JProperty("locality", Metadata.BuyerCity));
            dto.Buyer.Add(new JProperty("region", Metadata.BuyerState));
            dto.Buyer.Add(new JProperty("postalCode", Metadata.BuyerZip));
            dto.Buyer.Add(new JProperty("country", Metadata.BuyerCountry));
            dto.Buyer.Add(new JProperty("phone", Metadata.BuyerPhone));
            dto.Buyer.Add(new JProperty("email", string.IsNullOrWhiteSpace(Metadata.BuyerEmail) ? RefundMail : Metadata.BuyerEmail));

            dto.Token = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)); //No idea what it is useful for
            dto.Guid = Guid.NewGuid().ToString();
            return dto;
        }

        internal bool Support(PaymentMethodId paymentMethodId)
        {
            var rates = GetPaymentMethods();
            return rates.TryGet(paymentMethodId) != null;
        }

        public PaymentMethod GetPaymentMethod(PaymentMethodId paymentMethodId)
        {
            GetPaymentMethods().TryGetValue(paymentMethodId, out var data);
            return data;
        }
        public PaymentMethod GetPaymentMethod(BTCPayNetworkBase network, PaymentType paymentType)
        {
            return GetPaymentMethod(new PaymentMethodId(network.CryptoCode, paymentType));
        }

        public PaymentMethodDictionary GetPaymentMethods()
        {
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            var serializer = new Serializer(null);
#pragma warning disable CS0618
            if (PaymentMethod != null)
            {
                foreach (var prop in PaymentMethod.Properties())
                {
                    var r = serializer.ToObject<PaymentMethod>(prop.Value.ToString());
                    if (!PaymentMethodId.TryParse(prop.Name, out var paymentMethodId))
                    {
                        continue;
                    }
                    r.CryptoCode = paymentMethodId.CryptoCode;
                    r.PaymentType = paymentMethodId.PaymentType.ToString();
                    r.ParentEntity = this;
                    if (Networks != null)
                    {
                        r.Network = Networks.GetNetwork<BTCPayNetworkBase>(r.CryptoCode);
                        if (r.Network is null)
                            continue;
                    }
                    paymentMethods.Add(r);
                }
            }
#pragma warning restore CS0618
            return paymentMethods;
        }

        public void SetPaymentMethod(PaymentMethod paymentMethod)
        {
            var dict = GetPaymentMethods();
            dict.AddOrReplace(paymentMethod);
            SetPaymentMethods(dict);
        }

        public void SetPaymentMethods(PaymentMethodDictionary paymentMethods)
        {
            var obj = new JObject();
            var serializer = new Serializer(null);
#pragma warning disable CS0618
            foreach (var v in paymentMethods)
            {
                var clone = serializer.ToObject<PaymentMethod>(serializer.ToString(v));
                clone.CryptoCode = null;
                clone.PaymentType = null;
                obj.Add(new JProperty(v.GetId().ToString(), JObject.Parse(serializer.ToString(clone))));
            }
            PaymentMethod = obj;
            foreach (var cryptoData in paymentMethods)
            {
                cryptoData.ParentEntity = this;
            }
#pragma warning restore CS0618
        }

        public InvoiceState GetInvoiceState()
        {
            return new InvoiceState(Status, ExceptionStatus);
        }

        /// <summary>
        /// Invoice version < 1 were saving metadata directly under the InvoiceEntity
        /// object. But in version > 2, the metadata is saved under the InvoiceEntity.Metadata object
        /// This method is extracting metadata from the InvoiceEntity of version < 1 invoices and put them in InvoiceEntity.Metadata.
        /// </summary>
        internal void MigrateLegacyInvoice()
        {
            T TryParseMetadata<T>(string field) where T : class
            {
                if (AdditionalData.TryGetValue(field, out var token) && token is JObject obj)
                {
                    return obj.ToObject<T>();
                }
                return null;
            }
            if (TryParseMetadata<BuyerInformation>("buyerInformation") is BuyerInformation buyerInformation &&
                    TryParseMetadata<ProductInformation>("productInformation") is ProductInformation productInformation)
            {
                var wellknown = new InvoiceMetadata()
                {
                    BuyerAddress1 = buyerInformation.BuyerAddress1,
                    BuyerAddress2 = buyerInformation.BuyerAddress2,
                    BuyerCity = buyerInformation.BuyerCity,
                    BuyerCountry = buyerInformation.BuyerCountry,
                    BuyerEmail = buyerInformation.BuyerEmail,
                    BuyerName = buyerInformation.BuyerName,
                    BuyerPhone = buyerInformation.BuyerPhone,
                    BuyerState = buyerInformation.BuyerState,
                    BuyerZip = buyerInformation.BuyerZip,
                    ItemCode = productInformation.ItemCode,
                    ItemDesc = productInformation.ItemDesc,
                    Physical = productInformation.Physical,
                    TaxIncluded = productInformation.TaxIncluded
                };
                if (AdditionalData.TryGetValue("posData", out var token) &&
                    token is JValue val &&
                    val.Type == JTokenType.String)
                {
                    wellknown.PosData = val.Value<string>();
                }
                if (AdditionalData.TryGetValue("orderId", out var token2) &&
                    token2 is JValue val2 &&
                    val2.Type == JTokenType.String)
                {
                    wellknown.OrderId = val2.Value<string>();
                }
                Metadata = wellknown;
                Currency = productInformation.Currency?.Trim().ToUpperInvariant();
                Price = productInformation.Price;
            }
            else
            {
                throw new InvalidOperationException("Not a legacy invoice");
            }
        }

        public bool IsUnsetTopUp()
        {
            return Type == InvoiceType.TopUp && Price == 0.0m;
        }
    }

    public enum InvoiceStatusLegacy
    {
        New,
        Paid,
        Expired,
        Invalid,
        Complete,
        Confirmed
    }
    public static class InvoiceStatusLegacyExtensions
    {
        public static InvoiceStatus ToModernStatus(this InvoiceStatusLegacy legacy)
        {
            switch (legacy)
            {
                case InvoiceStatusLegacy.Complete:
                case InvoiceStatusLegacy.Confirmed:
                    return InvoiceStatus.Settled;
                case InvoiceStatusLegacy.Expired:
                    return InvoiceStatus.Expired;
                case InvoiceStatusLegacy.Invalid:
                    return InvoiceStatus.Invalid;
                case InvoiceStatusLegacy.Paid:
                    return InvoiceStatus.Processing;
                case InvoiceStatusLegacy.New:
                    return InvoiceStatus.New;
                default:
                    throw new NotSupportedException();
            }
        }
    }
    public class InvoiceState
    {
        static readonly Dictionary<string, InvoiceStatusLegacy> _StringToInvoiceStatus;
        static readonly Dictionary<InvoiceStatusLegacy, string> _InvoiceStatusToString;

        static readonly Dictionary<string, InvoiceExceptionStatus> _StringToExceptionStatus;
        static readonly Dictionary<InvoiceExceptionStatus, string> _ExceptionStatusToString;

        static InvoiceState()
        {
            _StringToInvoiceStatus = new Dictionary<string, InvoiceStatusLegacy>();
            _StringToInvoiceStatus.Add("paid", InvoiceStatusLegacy.Paid);
            _StringToInvoiceStatus.Add("expired", InvoiceStatusLegacy.Expired);
            _StringToInvoiceStatus.Add("invalid", InvoiceStatusLegacy.Invalid);
            _StringToInvoiceStatus.Add("complete", InvoiceStatusLegacy.Complete);
            _StringToInvoiceStatus.Add("new", InvoiceStatusLegacy.New);
            _StringToInvoiceStatus.Add("confirmed", InvoiceStatusLegacy.Confirmed);
            _InvoiceStatusToString = _StringToInvoiceStatus.ToDictionary(kv => kv.Value, kv => kv.Key);

            _StringToExceptionStatus = new Dictionary<string, InvoiceExceptionStatus>();
            _StringToExceptionStatus.Add(string.Empty, InvoiceExceptionStatus.None);
            _StringToExceptionStatus.Add("paidPartial", InvoiceExceptionStatus.PaidPartial);
            _StringToExceptionStatus.Add("paidLate", InvoiceExceptionStatus.PaidLate);
            _StringToExceptionStatus.Add("paidOver", InvoiceExceptionStatus.PaidOver);
            _StringToExceptionStatus.Add("marked", InvoiceExceptionStatus.Marked);
            _ExceptionStatusToString = _StringToExceptionStatus.ToDictionary(kv => kv.Value, kv => kv.Key);
            _StringToExceptionStatus.Add("false", InvoiceExceptionStatus.None);
        }
        public InvoiceState(string status, string exceptionStatus)
        {
            Status = _StringToInvoiceStatus[status];
            ExceptionStatus = _StringToExceptionStatus[exceptionStatus ?? string.Empty];
        }
        public InvoiceState(InvoiceStatusLegacy status, InvoiceExceptionStatus exceptionStatus)
        {
            Status = status;
            ExceptionStatus = exceptionStatus;
        }

        public InvoiceStatusLegacy Status { get; }
        public InvoiceExceptionStatus ExceptionStatus { get; }

        public static string ToString(InvoiceStatusLegacy status)
        {
            return _InvoiceStatusToString[status];
        }

        public static string ToString(InvoiceExceptionStatus exceptionStatus)
        {
            return _ExceptionStatusToString[exceptionStatus];
        }

        public bool CanMarkComplete()
        {
            return (Status == InvoiceStatusLegacy.Paid) ||
                   (Status == InvoiceStatusLegacy.New) ||
                   ((Status == InvoiceStatusLegacy.New || Status == InvoiceStatusLegacy.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidPartial) ||
                   ((Status == InvoiceStatusLegacy.New || Status == InvoiceStatusLegacy.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidLate) ||
                   (Status != InvoiceStatusLegacy.Complete && ExceptionStatus == InvoiceExceptionStatus.Marked) ||
                   (Status == InvoiceStatusLegacy.Invalid);
        }

        public bool CanMarkInvalid()
        {
            return (Status == InvoiceStatusLegacy.Paid) ||
                   (Status == InvoiceStatusLegacy.New) ||
                   ((Status == InvoiceStatusLegacy.New || Status == InvoiceStatusLegacy.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidPartial) ||
                   ((Status == InvoiceStatusLegacy.New || Status == InvoiceStatusLegacy.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidLate) ||
                   (Status != InvoiceStatusLegacy.Invalid && ExceptionStatus == InvoiceExceptionStatus.Marked);
        }

        public bool CanRefund()
        {
            return Status == InvoiceStatusLegacy.Confirmed ||
                Status == InvoiceStatusLegacy.Complete ||
                (Status == InvoiceStatusLegacy.Expired &&
                (ExceptionStatus == InvoiceExceptionStatus.PaidLate ||
                ExceptionStatus == InvoiceExceptionStatus.PaidOver ||
                ExceptionStatus == InvoiceExceptionStatus.PaidPartial)) ||
                Status == InvoiceStatusLegacy.Invalid;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Status, ExceptionStatus);
        }

        public static bool operator ==(InvoiceState a, InvoiceState b)
        {
            if (a is null && b is null)
                return true;
            if (a is null)
                return false;
            return a.Equals(b);
        }

        public static bool operator !=(InvoiceState a, InvoiceState b)
        {
            return !(a == b);
        }

        public bool Equals(InvoiceState o)
        {
            if (o is null)
                return false;
            return o.Status == Status && o.ExceptionStatus == ExceptionStatus;
        }
        public override bool Equals(object obj)
        {
            if (obj is InvoiceState o)
            {
                return this.Equals(o);
            }
            return false;
        }
        public override string ToString()
        {
            return Status.ToModernStatus().ToString() + (ExceptionStatus == InvoiceExceptionStatus.None ? string.Empty : $" ({ToString(ExceptionStatus)})");
        }
    }

    public class PaymentMethodAccounting
    {
        /// <summary>Total amount of this invoice</summary>
        public Money TotalDue { get; set; }

        /// <summary>Amount of crypto remaining to pay this invoice</summary>
        public Money Due { get; set; }

        /// <summary>Same as Due, can be negative</summary>
        public Money DueUncapped { get; set; }

        /// <summary>If DueUncapped is negative, that means user overpaid invoice</summary>
        public Money OverpaidHelper
        {
            get { return DueUncapped > Money.Zero ? Money.Zero : -DueUncapped; }
        }

        /// <summary>
        /// Total amount of the invoice paid after conversion to this crypto currency
        /// </summary>
        public Money Paid { get; set; }

        /// <summary>
        /// Total amount of the invoice paid in this currency
        /// </summary>
        public Money CryptoPaid { get; set; }

        /// <summary>
        /// Number of transactions required to pay
        /// </summary>
        public int TxRequired { get; set; }

        /// <summary>
        /// Number of transactions using this payment method
        /// </summary>
        public int TxCount { get; set; }
        /// <summary>
        /// Total amount of network fee to pay to the invoice
        /// </summary>
        public Money NetworkFee { get; set; }
        /// <summary>
        /// Total amount of network fee to pay to the invoice
        /// </summary>
        public Money NetworkFeeAlreadyPaid { get; set; }
        /// <summary>
        /// Minimum required to be paid in order to accept invoice as paid
        /// </summary>
        public Money MinimumTotalDue { get; set; }
    }

    public interface IPaymentMethod
    {
        PaymentMethodId GetId();
        decimal Rate { get; set; }
        IPaymentMethodDetails GetPaymentMethodDetails();
    }

    public class PaymentMethod : IPaymentMethod
    {
        [JsonIgnore]
        public InvoiceEntity ParentEntity { get; set; }
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; }
        [JsonProperty(PropertyName = "cryptoCode", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [Obsolete("Use GetId().CryptoCode instead")]
        public string CryptoCode { get; set; }
        [JsonProperty(PropertyName = "paymentType", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [Obsolete("Use GetId().PaymentType instead")]
        public string PaymentType { get; set; }

        /// <summary>
        /// We only use this to pass a singleton asking to the payment handler to prefer payments through TOR, we don't really
        /// need to save this information
        /// </summary>
        [JsonIgnore]
        public bool PreferOnion { get; set; }

        public PaymentMethodId GetId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new PaymentMethodId(CryptoCode, string.IsNullOrEmpty(PaymentType) ? PaymentTypes.BTCLike : PaymentTypes.Parse(PaymentType));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetId(PaymentMethodId id)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            CryptoCode = id.CryptoCode;
            PaymentType = id.PaymentType.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonProperty(PropertyName = "rate")]
        public decimal Rate { get; set; }

        [Obsolete("Use GetPaymentMethodDetails() instead")]
        [JsonProperty(PropertyName = "paymentMethod")]
        public JObject PaymentMethodDetails { get; set; }
        public IPaymentMethodDetails GetPaymentMethodDetails()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            // Legacy, old code does not have PaymentMethods
            if (string.IsNullOrEmpty(PaymentType) || PaymentMethodDetails == null)
            {
                return new BitcoinLikeOnChainPaymentMethod
                {
                    FeeRate = FeeRate,
                    DepositAddress = string.IsNullOrEmpty(DepositAddress) ? null : DepositAddress,
                    NextNetworkFee = NextNetworkFee
                };
            }
            
            IPaymentMethodDetails details = GetId().PaymentType.DeserializePaymentMethodDetails(Network, PaymentMethodDetails.ToString());
            switch (details)
            {
                case BitcoinLikeOnChainPaymentMethod btcLike:
                    btcLike.NextNetworkFee = NextNetworkFee;
                    btcLike.DepositAddress = string.IsNullOrEmpty(DepositAddress) ? null : DepositAddress;
                    btcLike.FeeRate = FeeRate;
                    break;
                case LightningLikePaymentMethodDetails lnLike:
                    // use set properties and fall back to values from payment data
                    var payments = ParentEntity.GetPayments(true).Where(paymentEntity =>
                        paymentEntity.GetPaymentMethodId() == GetId());
                    var payment = payments.Select(p => p.GetCryptoPaymentData() as LightningLikePaymentData).FirstOrDefault();
                    var paymentHash = payment?.PaymentHash != null && payment.PaymentHash != default ? payment.PaymentHash : null;
                    var preimage = payment?.Preimage != null && payment.Preimage != default ? payment.Preimage : null;
                    lnLike.PaymentHash = lnLike.PaymentHash != null && lnLike.PaymentHash != default ? lnLike.PaymentHash : paymentHash;
                    lnLike.Preimage = lnLike.Preimage != null && lnLike.Preimage != default ? lnLike.Preimage : preimage;
                    break;
            }

            return details;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public PaymentMethod SetPaymentMethodDetails(IPaymentMethodDetails paymentMethod)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            // Legacy, need to fill the old fields

            if (PaymentType == null)
                PaymentType = paymentMethod.GetPaymentType().ToString();
            else if (PaymentType != paymentMethod.GetPaymentType().ToString())
                throw new InvalidOperationException("Invalid payment method affected");

            if (paymentMethod is Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod bitcoinPaymentMethod)
            {
                NextNetworkFee = bitcoinPaymentMethod.NextNetworkFee;
                FeeRate = bitcoinPaymentMethod.FeeRate;
                DepositAddress = bitcoinPaymentMethod.DepositAddress;
            }
            PaymentMethodDetails = JObject.Parse(paymentMethod.GetPaymentType().SerializePaymentMethodDetails(Network, paymentMethod));

#pragma warning restore CS0618 // Type or member is obsolete
            return this;
        }

        [JsonProperty(PropertyName = "feeRate")]
        [Obsolete("Use ((BitcoinLikeOnChainPaymentMethod)GetPaymentMethod()).FeeRate")]
        public FeeRate FeeRate { get; set; }
        [JsonProperty(PropertyName = "txFee")]
        [Obsolete("Use ((BitcoinLikeOnChainPaymentMethod)GetPaymentMethod()).NextNetworkFee")]
        public Money NextNetworkFee { get; set; }
        [JsonProperty(PropertyName = "depositAddress")]
        [Obsolete("Use ((BitcoinLikeOnChainPaymentMethod)GetPaymentMethod()).DepositAddress")]
        public string DepositAddress { get; set; }

        public PaymentMethodAccounting Calculate(Func<PaymentEntity, bool> paymentPredicate = null)
        {
            paymentPredicate = paymentPredicate ?? new Func<PaymentEntity, bool>((p) => true);
            var paymentMethods = ParentEntity.GetPaymentMethods();

            var totalDue = ParentEntity.Price / Rate;
            var paid = 0m;
            var cryptoPaid = 0.0m;

            int precision = Network?.Divisibility ?? 8;
            var totalDueNoNetworkCost = Money.Coins(Extensions.RoundUp(totalDue, precision));
            bool paidEnough = paid >= Extensions.RoundUp(totalDue, precision);
            int txRequired = 0;
            decimal networkFeeAlreadyPaid = 0.0m;
            _ = ParentEntity.GetPayments(true)
                .Where(p => paymentPredicate(p))
                .OrderBy(p => p.ReceivedTime)
                .Select(_ =>
                {
                    var txFee = _.GetValue(paymentMethods, GetId(), _.NetworkFee, precision);
                    networkFeeAlreadyPaid += txFee;
                    paid += _.GetValue(paymentMethods, GetId(), null, precision);
                    if (!paidEnough)
                    {
                        totalDue += txFee;
                    }

                    paidEnough |= Extensions.RoundUp(paid, precision) >= Extensions.RoundUp(totalDue, precision);
                    if (GetId() == _.GetPaymentMethodId())
                    {
                        cryptoPaid += _.GetCryptoPaymentData().GetValue();
                        txRequired++;
                    }

                    return _;
                }).ToArray();

            var accounting = new PaymentMethodAccounting();
            accounting.TxCount = txRequired;
            if (!paidEnough)
            {
                txRequired++;
                totalDue += GetTxFee();
            }

            accounting.TotalDue = Money.Coins(Extensions.RoundUp(totalDue, precision));
            accounting.Paid = Money.Coins(Extensions.RoundUp(paid, precision));
            accounting.TxRequired = txRequired;
            accounting.CryptoPaid = Money.Coins(Extensions.RoundUp(cryptoPaid, precision));
            accounting.Due = Money.Max(accounting.TotalDue - accounting.Paid, Money.Zero);
            accounting.DueUncapped = accounting.TotalDue - accounting.Paid;
            accounting.NetworkFee = accounting.TotalDue - totalDueNoNetworkCost;
            accounting.NetworkFeeAlreadyPaid = Money.Coins(Extensions.RoundUp(networkFeeAlreadyPaid, precision));
            // If the total due is 0, there is no payment tolerance to calculate
            var minimumTotalDueSatoshi = accounting.TotalDue.Satoshi == 0
                ? 0
                : Math.Max(1.0m,
                    accounting.TotalDue.Satoshi * (1.0m - ((decimal)ParentEntity.PaymentTolerance / 100.0m)));
            accounting.MinimumTotalDue = Money.Satoshis(minimumTotalDueSatoshi);
            return accounting;
        }

        private decimal GetTxFee()
        {
            return GetPaymentMethodDetails()?.GetNextNetworkFee() ?? 0m;
        }
    }

    public class PaymentEntity
    {
        [NotMapped]
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; }
        public int Version { get; set; }

        [Obsolete("Use ReceivedTime instead")]
        [JsonProperty("receivedTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
        // Old invoices were storing the received time in second
        public DateTimeOffset? ReceivedTimeSeconds
        {
            get; set;
        }
        [Obsolete("Use ReceivedTime instead")]
        [JsonProperty("receivedTimeMs", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [JsonConverter(typeof(DateTimeMilliJsonConverter))]
        // Our RBF detection logic depends on properly ordering payments based on
        // received time, so we needed a received time in milli to ensure that
        // even if payments are separated by less than a second, they would still be ordered correctly
        public DateTimeOffset? ReceivedTimeMilli
        {
            get; set;
        }
        [JsonIgnore]
        public DateTimeOffset ReceivedTime
        {
            get
            {
#pragma warning disable 618
                return (ReceivedTimeMilli ?? ReceivedTimeSeconds).GetValueOrDefault();
#pragma warning restore 618
            }
            set
            {
#pragma warning disable 618
                ReceivedTimeMilli = value;
#pragma warning restore 618
            }
        }
        public decimal NetworkFee { get; set; }
        [Obsolete("Use ((BitcoinLikePaymentData)GetCryptoPaymentData()).Outpoint")]
        public OutPoint Outpoint
        {
            get; set;
        }

        [Obsolete("Use ((BitcoinLikePaymentData)GetCryptoPaymentData()).Output")]
        public TxOut Output
        {
            get; set;
        }

        public bool Accounted
        {
            get; set;
        }


        [Obsolete("Use GetpaymentMethodId().CryptoCode instead")]
        public string CryptoCode
        {
            get;
            set;
        }

        [Obsolete("Use GetCryptoPaymentData() instead")]
        public string CryptoPaymentData { get; set; }
        [Obsolete("Use GetpaymentMethodId().PaymentType instead")]
        public string CryptoPaymentDataType { get; set; }


        public CryptoPaymentData GetCryptoPaymentData()
        {
            CryptoPaymentData paymentData = null;
#pragma warning disable CS0618 // Type or member is obsolete
            if (string.IsNullOrEmpty(CryptoPaymentData))
            {
                // For invoices created when CryptoPaymentDataType was not existing, we just consider that it is a RBFed payment for safety
                var bitcoin = new BitcoinLikePaymentData();
                bitcoin.Network = Network;
                bitcoin.Outpoint = Outpoint;
                bitcoin.Output = Output;
                bitcoin.RBF = true;
                bitcoin.ConfirmationCount = 0;
                bitcoin.Legacy = true;
                bitcoin.Output = Output;
                bitcoin.Outpoint = Outpoint;
                paymentData = bitcoin;
            }
            else
            {
                var paymentMethodId = GetPaymentMethodId();
                if (paymentMethodId is null)
                {
                    return null;
                }

                paymentData = paymentMethodId.PaymentType.DeserializePaymentData(Network, CryptoPaymentData);
                if (paymentData is null)
                {
                    return null;
                }

                paymentData.Network = Network;
                if (paymentData is BitcoinLikePaymentData bitcoin)
                {
                    bitcoin.Output = Output;
                    bitcoin.Outpoint = Outpoint;
                }
            }
            return paymentData;
        }

        public PaymentEntity SetCryptoPaymentData(CryptoPaymentData cryptoPaymentData)
        {
#pragma warning disable CS0618
            if (cryptoPaymentData is Payments.Bitcoin.BitcoinLikePaymentData paymentData)
            {
                // Legacy
                Outpoint = paymentData.Outpoint;
                Output = paymentData.Output;
                ///
            }
            CryptoPaymentDataType = cryptoPaymentData.GetPaymentType().ToString();
            CryptoPaymentData = GetPaymentMethodId().PaymentType.SerializePaymentData(Network, cryptoPaymentData);
#pragma warning restore CS0618
            return this;
        }
        internal decimal GetValue(PaymentMethodDictionary paymentMethods, PaymentMethodId paymentMethodId, decimal? value, int precision)
        {

            value = value ?? this.GetCryptoPaymentData().GetValue();
            var to = paymentMethodId;
            var from = this.GetPaymentMethodId();
            if (to == from)
                return decimal.Round(value.Value, precision);
            var fromRate = paymentMethods[from].Rate;
            var toRate = paymentMethods[to].Rate;

            var fiatValue = fromRate * decimal.Round(value.Value, precision);
            var otherCurrencyValue = toRate == 0 ? 0.0m : fiatValue / toRate;
            return otherCurrencyValue;
        }

        public PaymentMethodId GetPaymentMethodId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            PaymentType paymentType;
            if (string.IsNullOrEmpty(CryptoPaymentDataType))
            {
                paymentType = BitcoinPaymentType.Instance;
                ;
            }
            else if (!PaymentTypes.TryParse(CryptoPaymentDataType, out paymentType))
            {
                return null;
            }
            return new PaymentMethodId(CryptoCode ?? "BTC", paymentType);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public string GetCryptoCode()
        {
#pragma warning disable CS0618
            return CryptoCode ?? "BTC";
#pragma warning restore CS0618
        }
    }
    /// <summary>
    /// A record of a payment
    /// </summary>
    public interface CryptoPaymentData
    {
        [JsonIgnore]
        BTCPayNetworkBase Network { get; set; }
        /// <summary>
        /// Returns an identifier which uniquely identify the payment
        /// </summary>
        /// <returns>The payment id</returns>
        string GetPaymentId();

        /// <summary>
        /// Returns terms which will be indexed and searchable in the search bar of invoice
        /// </summary>
        /// <returns>The search terms</returns>
        string[] GetSearchTerms();
        /// <summary>
        /// Get value of what as been paid
        /// </summary>
        /// <returns>The amount paid</returns>
        decimal GetValue();
        bool PaymentCompleted(PaymentEntity entity);
        bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy);

        PaymentType GetPaymentType();
        string GetDestination();
    }
}
