using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.JsonConverters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using static BTCPayServer.Controllers.BitpayRateController;

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
    public class InvoiceMetadata : IHasAdditionalData
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
            get => this.GetAdditionalData<string>("orderId");
            set => this.SetAdditionalData("orderId", value);
        }
        [JsonIgnore]
        public string OrderUrl
        {
            get => this.GetAdditionalData<string>("orderUrl");
            set => this.SetAdditionalData("orderUrl", value);
        }
        [JsonIgnore]
        public string PaymentRequestId
        {
            get => this.GetAdditionalData<string>("paymentRequestId");
            set => this.SetAdditionalData("paymentRequestId", value);
        }
        [JsonIgnore]
        public string BuyerName
        {
            get => this.GetAdditionalData<string>("buyerName");
            set => this.SetAdditionalData("buyerName", value);
        }
        [JsonIgnore]
        public string BuyerEmail
        {
            get => this.GetAdditionalData<string>("buyerEmail");
            set => this.SetAdditionalData("buyerEmail", value);
        }
        [JsonIgnore]
        public string BuyerCountry
        {
            get => this.GetAdditionalData<string>("buyerCountry");
            set => this.SetAdditionalData("buyerCountry", value);
        }
        [JsonIgnore]
        public string BuyerZip
        {
            get => this.GetAdditionalData<string>("buyerZip");
            set => this.SetAdditionalData("buyerZip", value);
        }
        [JsonIgnore]
        public string BuyerState
        {
            get => this.GetAdditionalData<string>("buyerState");
            set => this.SetAdditionalData("buyerState", value);
        }
        [JsonIgnore]
        public string BuyerCity
        {
            get => this.GetAdditionalData<string>("buyerCity");
            set => this.SetAdditionalData("buyerCity", value);
        }
        [JsonIgnore]
        public string BuyerAddress2
        {
            get => this.GetAdditionalData<string>("buyerAddress2");
            set => this.SetAdditionalData("buyerAddress2", value);
        }
        [JsonIgnore]
        public string BuyerAddress1
        {
            get => this.GetAdditionalData<string>("buyerAddress1");
            set => this.SetAdditionalData("buyerAddress1", value);
        }
        [JsonIgnore]
        public string BuyerPhone
        {
            get => this.GetAdditionalData<string>("buyerPhone");
            set => this.SetAdditionalData("buyerPhone", value);
        }
        [JsonIgnore]
        public string ItemDesc
        {
            get => this.GetAdditionalData<string>("itemDesc");
            set => this.SetAdditionalData("itemDesc", value);
        }
        [JsonIgnore]
        public string ItemCode
        {
            get => this.GetAdditionalData<string>("itemCode");
            set => this.SetAdditionalData("itemCode", value);
        }
        [JsonIgnore]
        public bool? Physical
        {
            get => this.GetAdditionalData<bool?>("physical");
            set => this.SetAdditionalData("physical", value);
        }
        [JsonIgnore]
        public decimal? TaxIncluded
        {
            get => this.GetAdditionalData<decimal?>("taxIncluded");
            set => this.SetAdditionalData("taxIncluded", value);
        }

        /// <summary>
        /// posData is a field that may be treated differently for presentation and in some legacy API
        /// Before, it was a string field which could contain some JSON data inside.
        /// For making it easier to query on the DB, and for logic using PosData in the code, we decided to
        /// parse it as a JObject.
        ///
        /// This property will return the posData as a JObject, even if it's a Json string inside.
        /// </summary>
        [JsonIgnore]
        public JObject PosData
        {
            get
            {
                if (AdditionalData == null || !(AdditionalData.TryGetValue("posData", out var jt) is true))
                    return default;
                if (jt.Type == JTokenType.Null)
                    return default;
                if (jt.Type == JTokenType.String)
                    try
                    {
                        return JObject.Parse(jt.Value<string>());
                    }
                    catch
                    {
                        return null;
                    }
                if (jt.Type == JTokenType.Object)
                    return (JObject)jt;
                return null;
            }

            set
            {
                this.SetAdditionalData<JObject>("posData", value);
            }
        }

        /// <summary>
        /// See comments on <see cref="PosData"/>
        /// </summary>
        [JsonIgnore]
        public string PosDataLegacy
        {
            get
            {
                return this.GetAdditionalData<string>("posData");
            }

            set
            {
                if (value != null)
                {
                    try
                    {
                        PosData = JObject.Parse(value);
                        return;
                    }
                    catch
                    {
                    }
                }
                this.SetAdditionalData<string>("posData", value);
            }
        }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }

        public static InvoiceMetadata FromJObject(JObject jObject)
        {
            return jObject.ToObject<InvoiceMetadata>(MetadataSerializer);
        }
        public JObject ToJObject()
        {
            return JObject.FromObject(this, MetadataSerializer);
        }
    }

    public class InvoiceEntity : IHasAdditionalData
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
        public const int InternalTagSupport_Version = 1;
        public const int GreenfieldInvoices_Version = 2;
        public const int LeanInvoices_Version = 3;
        public const int Lastest_Version = 3;
        public int Version { get; set; }
        [JsonIgnore]
        public string Id { get; set; }
        [JsonIgnore]
        public string StoreId { get; set; }

        public SpeedPolicy SpeedPolicy { get; set; }
        [JsonProperty]
        public string DefaultLanguage { get; set; }
        [JsonIgnore]
        public DateTimeOffset InvoiceTime { get; set; }
        public DateTimeOffset ExpirationTime { get; set; }
        public InvoiceMetadata Metadata { get; set; }
        [JsonIgnore]
        public decimal Price { get; set; }
        [JsonIgnore]
        public string Currency { get; set; }
        [JsonConverter(typeof(PaymentMethodIdJsonConverter))]
        public PaymentMethodId DefaultPaymentMethod { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }

        [JsonProperty]
        public HashSet<string> InternalTags { get; set; } = new HashSet<string>();

        public string[] GetInternalTags(string prefix)
        {
            return InternalTags == null ? Array.Empty<string>() : InternalTags
                                                  .Where(t => t.StartsWith(prefix, StringComparison.InvariantCulture))
                                                  .Select(t => t.Substring(prefix.Length)).ToArray();
        }

        public decimal GetInvoiceRate(string currency)
        {
            ArgumentNullException.ThrowIfNull(currency);
            if (Currency is null)
                throw new InvalidOperationException("The Currency of the invoice isn't set");
            return GetRate(new CurrencyPair(currency, Currency));
        }
        public RateRules GetRateRules()
        {
            StringBuilder builder = new StringBuilder();
#pragma warning disable CS0618 // Type or member is obsolete
            foreach (var r in Rates)
            {
                if (r.Key.Contains('_', StringComparison.Ordinal))
                    builder.AppendLine($"{r.Key} = {r.Value.ToString(CultureInfo.InvariantCulture)};");
                else
                    builder.AppendLine($"{r.Key}_{Currency} = {r.Value.ToString(CultureInfo.InvariantCulture)};");
            }
#pragma warning restore CS0618 // Type or member is obsolete
            if (RateRules.TryParse(builder.ToString(), out var rules))
                return rules;
            throw new FormatException("Invalid rate rules");
        }
        public bool TryGetRate(string currency, out decimal rate)
        {
            return TryGetRate(new CurrencyPair(currency, Currency), out rate);
        }
        public bool TryGetRate(CurrencyPair pair, out decimal rate)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (pair.Right == Currency && Rates.TryGetValue(pair.Left, out rate)) // Fast lane
                return true;
#pragma warning restore CS0618 // Type or member is obsolete
            var rule = GetRateRules().GetRuleFor(pair);
            rule.Reevaluate();
            if (rule.BidAsk is null)
            {
                rate = 0.0m;
                return false;
            }
            rate = rule.BidAsk.Bid;
            return true;
        }
        public decimal GetRate(CurrencyPair pair)
        {
            ArgumentNullException.ThrowIfNull(pair);
#pragma warning disable CS0618 // Type or member is obsolete
            if (pair.Right == Currency && Rates.TryGetValue(pair.Left, out var rate)) // Fast lane
                return rate;
#pragma warning restore CS0618 // Type or member is obsolete
            var rule = GetRateRules().GetRuleFor(pair);
            rule.Reevaluate();
            if (rule.BidAsk is null)
                throw new InvalidOperationException($"Rate rule is not evaluated ({rule.Errors.First()})");
            return rule.BidAsk.Bid;
        }
        public void AddRate(CurrencyPair pair, decimal rate)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var v = pair.Right == Currency ? pair.Left : pair.ToString();
            Rates.Add(v, rate);
#pragma warning restore CS0618 // Type or member is obsolete
        }
        [Obsolete("Use GetRate instead")]
        [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
        public Dictionary<string, decimal> Rates
        {
            get;
            set;
        } = new Dictionary<string, decimal>();

#nullable enable
        public PaymentMethodId? GetDefaultPaymentMethodId(Data.StoreData store, BTCPayNetworkProvider networkProvider, HashSet<PaymentMethodId>? authorized = null)
        {
            PaymentMethodId? paymentMethodId = null;
            PaymentMethodId? invoicePaymentId = DefaultPaymentMethod;
            PaymentMethodId? storePaymentId = store.GetDefaultPaymentId();
            authorized ??= GetPaymentPrompts().Select(p => p.PaymentMethodId).ToHashSet();
            if (invoicePaymentId is not null)
            {
                if (authorized.Contains(invoicePaymentId))
                    paymentMethodId = invoicePaymentId;
            }
            if (paymentMethodId is null && storePaymentId is not null)
            {
                if (authorized.Contains(storePaymentId))
                    paymentMethodId = storePaymentId;
            }
            if (paymentMethodId is null && invoicePaymentId is not null)
            {
                paymentMethodId = invoicePaymentId.FindNearest(authorized);
            }
            if (paymentMethodId is null && storePaymentId is not null)
            {
                paymentMethodId = storePaymentId.FindNearest(authorized);
            }
            if (paymentMethodId is null)
            {
                var defaultBTC = PaymentTypes.CHAIN.GetPaymentMethodId(networkProvider.DefaultNetwork.CryptoCode);
                var defaultLNURLPay = PaymentTypes.LNURL.GetPaymentMethodId(networkProvider.DefaultNetwork.CryptoCode);
                paymentMethodId = authorized.FirstOrDefault(e => e == defaultBTC) ??
                                  authorized.FirstOrDefault(e => e == defaultLNURLPay) ??
                                  authorized.FirstOrDefault();
            }

            return paymentMethodId;
        }
#nullable restore

        public void UpdateTotals()
        {
            if (DisableAccounting)
                throw new InvalidOperationException("Accounting disabled, impossible to call UpdateTotals");
            PaidAmount = new Amounts()
            {
                Currency = Currency
            };
            NetSettled = 0.0m;
            foreach (var payment in GetPayments(false))
            {
                payment.Rate = GetInvoiceRate(payment.Currency);
                payment.InvoiceEntity = this;
                payment.UpdateAmounts();
                if (payment.Accounted)
                {
                    PaidAmount.Gross += payment.InvoicePaidAmount.Gross;
                    PaidAmount.Net += payment.InvoicePaidAmount.Net;
                    if (payment.Status == PaymentStatus.Settled)
                        NetSettled += payment.InvoicePaidAmount.Net;
                }
            }
            NetDue = Price - PaidAmount.Net;
            MinimumNetDue = Price * (1.0m - ((decimal)PaymentTolerance / 100.0m)) - PaidAmount.Net;
            PaidFee = PaidAmount.Gross - PaidAmount.Net;
            if (NetDue < 0.0m)
            {
                // If any payment method exactly pay the invoice, the overpayment is caused by
                // rounding limitation of the underlying payment method.
                // Document this overpayment as dust, and set the net due to 0
                if (GetPaymentPrompts().Any(p => p.Calculate().DueUncapped == 0.0m))
                {
                    Dust = -NetDue;
                    NetDue = 0.0m;
                }
            }
        }

        /// <summary>
        /// Overpaid amount caused by payment method
        /// Example: If you need to pay 124.4 sats, the on-chain payment need to be technically rounded to 125 sats, the extra 0.6 sats shouldn't be considered an over payment.
        /// </summary>
        [JsonIgnore]
        public decimal Dust { get; set; }

        /// <summary>
        /// The due to consider the invoice paid (can be negative if over payment)
        /// </summary>
        [JsonIgnore]
        public decimal NetDue
        {
            get;
            set;
        }
        /// <summary>
        /// Minimum due to consider the invoice paid (can be negative if overpaid)
        /// </summary>
        [JsonIgnore]
        public decimal MinimumNetDue { get; set; }
        [JsonIgnore]
        public bool IsUnderPaid => MinimumNetDue > 0;
        [JsonIgnore]
        public bool IsOverPaid => NetDue < 0;


        /// <summary>
        /// Total of network fee paid by accounted payments
        /// </summary>
        [JsonIgnore]
        public decimal PaidFee { get; set; }

        [JsonIgnore]
        public InvoiceStatus Status { get; set; }
        [JsonIgnore]
        public InvoiceExceptionStatus ExceptionStatus { get; set; }

        [Obsolete("Use GetPayments instead")]
        [JsonIgnore]
        public List<PaymentEntity> Payments { get; set; }

#pragma warning disable CS0618
        public List<PaymentEntity> GetPayments(bool accountedOnly)
        {
            return Payments?.Where(entity => (!accountedOnly || entity.Accounted)).ToList() ?? new List<PaymentEntity>();
        }
        public List<PaymentEntity> GetPayments(string currency, bool accountedOnly)
        {
            return GetPayments(accountedOnly).Where(p => p.Currency == currency).ToList();
        }
#pragma warning restore CS0618

        [JsonProperty]
        public string StoreSupportUrl { get; set; }
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

        [JsonProperty]
        public bool RedirectAutomatically { get; set; }
        public bool FullNotifications { get; set; }
        [JsonProperty]
        public string NotificationEmail { get; set; }

        [JsonProperty("notificationURL")]
        public string NotificationURLTemplate { get; set; }

        [JsonIgnore]
        public Uri NotificationURL => FillPlaceholdersUri(NotificationURLTemplate);
        public string ServerUrl { get; set; }

        [Obsolete("Use Set/GetPaymentPrompts() instead")]
        [JsonProperty(PropertyName = "prompts")]
        public JObject PaymentPrompts { get; set; }

        [JsonProperty]
        public DateTimeOffset MonitoringExpiration { get; set; }

        [JsonIgnore]
        public HashSet<(PaymentMethodId PaymentMethodId, string Address)> Addresses { get; set; }
        [JsonProperty]
        public bool ExtendedNotifications { get; set; }

        [JsonProperty]
        public double PaymentTolerance { get; set; }
        [JsonIgnore]
        public bool Archived { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty]
        public InvoiceType Type { get; set; }

        [JsonIgnore]
        public List<RefundData> Refunds { get; set; }

        [JsonProperty]
        public InvoiceDataBase.ReceiptOptions ReceiptOptions { get; set; }

        [JsonProperty]
        public bool LazyPaymentMethods { get; set; }

        public bool IsExpired()
        {
            return DateTimeOffset.UtcNow > ExpirationTime;
        }

        public InvoiceResponse EntityToDTO(IDictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension> bitpayExtensions, CurrencyNameTable currencyNameTable)
        {
            return EntityToDTO(bitpayExtensions, null, currencyNameTable);
        }
        public InvoiceResponse EntityToDTO(IDictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension> bitpayExtensions, IUrlHelper urlHelper, CurrencyNameTable currencyNameTable)
        {
            ServerUrl = ServerUrl ?? "";
            InvoiceResponse dto = new InvoiceResponse
            {
                Id = Id,
                StoreId = StoreId,
                OrderId = Metadata.OrderId,
                PosData = Metadata.PosDataLegacy,
                CurrentTime = DateTimeOffset.UtcNow,
                InvoiceTime = InvoiceTime,
                ExpirationTime = ExpirationTime,
                Status = Status.ToLegacyStatusString(),
                ExceptionStatus = ExceptionStatus == InvoiceExceptionStatus.None ? new JValue(false) : new JValue(ExceptionStatus.ToLegacyExceptionStatusString()),
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
            foreach (var info in this.GetPaymentPrompts())
            {
                var accounting = info.Calculate();
                var cryptoInfo = new InvoiceCryptoInfo();
                var subtotalPrice = accounting.TotalDue - accounting.PaymentMethodFee;
                var cryptoCode = info.Currency;
                var address = info.Destination;
                var exrates = new Dictionary<string, decimal>
                {
                    { Currency, cryptoInfo.Rate }
                };

                cryptoInfo.CryptoCode = cryptoCode;
                cryptoInfo.PaymentType = info.PaymentMethodId.ToString();
                cryptoInfo.Rate = info.Rate;
                cryptoInfo.Price = subtotalPrice.ToString(CultureInfo.InvariantCulture);

                cryptoInfo.Due = accounting.Due.ToString(CultureInfo.InvariantCulture);
                cryptoInfo.Paid = accounting.Paid.ToString(CultureInfo.InvariantCulture);
                cryptoInfo.TotalDue = accounting.TotalDue.ToString(CultureInfo.InvariantCulture);
                cryptoInfo.NetworkFee = accounting.PaymentMethodFee.ToString(CultureInfo.InvariantCulture);
                cryptoInfo.TxCount = accounting.TxCount;
                cryptoInfo.CryptoPaid = accounting.PaymentMethodPaid.ToString(CultureInfo.InvariantCulture);

                cryptoInfo.Address = address;

                cryptoInfo.ExRates = exrates;
                var paymentId = info.PaymentMethodId;
                cryptoInfo.Url = ServerUrl.WithTrailingSlash() + $"i/{paymentId}/{Id}";
                cryptoInfo.Payments = GetPayments(info.Currency, true).Select(entity =>
                {
                    return new InvoicePaymentInfo()
                    {
                        Id = entity.Id,
                        Fee = entity.PaymentMethodFee,
                        Value = entity.Value,
                        Completed = entity.Status is PaymentStatus.Settled,
                        Confirmed = entity.Status is PaymentStatus.Settled,
                        Destination = entity.Destination,
                        PaymentType = entity.PaymentMethodId.ToString(),
                        ReceivedDate = entity.ReceivedTime.DateTime
                    };
                }).ToList();


                if (info.Activated)
                {

                    if (bitpayExtensions.TryGetValue(paymentId, out var e))
                        e.PopulateCryptoInfo(cryptoInfo, dto, info, urlHelper);
                }

                dto.CryptoInfo.Add(cryptoInfo);
                // Ideally, this should just be the payment id, but this
                // is for legacy compatibility with the Bitpay API
                var paymentCode = GetPaymentCode(info.Currency, paymentId);
                dto.PaymentCodes.Add(paymentCode, cryptoInfo.PaymentUrls);
                if (info.Currency is not null && currencyNameTable.GetCurrencyData(info.Currency, true)?.Divisibility is int divisibility)
                {
                    dto.PaymentSubtotals.Add(paymentCode, BitcoinPaymentMethodBitpayAPIExtension.ToSmallestUnit(divisibility, subtotalPrice));
                    dto.PaymentTotals.Add(paymentCode, BitcoinPaymentMethodBitpayAPIExtension.ToSmallestUnit(divisibility, accounting.TotalDue));
                }
                dto.SupportedTransactionCurrencies.TryAdd(cryptoCode, new InvoiceSupportedTransactionCurrency()
                {
                    Enabled = true
                });
                dto.Addresses.Add(paymentCode, address);
                dto.ExchangeRates.TryAdd(cryptoCode, exrates);
            }

            //dto.AmountPaid dto.MinerFees & dto.TransactionCurrency are not supported by btcpayserver as we have multi currency payment support per invoice

            dto.ItemCode = Metadata.ItemCode;
            dto.ItemDesc = Metadata.ItemDesc;
            dto.TaxIncluded = Metadata.TaxIncluded ?? 0m;
            dto.Price = Price;
            dto.Currency = Currency;
            dto.Buyer = new JObject();
            dto.Buyer.Add(new JProperty("name", Metadata.BuyerName));
            dto.Buyer.Add(new JProperty("address1", Metadata.BuyerAddress1));
            dto.Buyer.Add(new JProperty("address2", Metadata.BuyerAddress2));
            dto.Buyer.Add(new JProperty("locality", Metadata.BuyerCity));
            dto.Buyer.Add(new JProperty("region", Metadata.BuyerState));
            dto.Buyer.Add(new JProperty("postalCode", Metadata.BuyerZip));
            dto.Buyer.Add(new JProperty("country", Metadata.BuyerCountry));
            dto.Buyer.Add(new JProperty("phone", Metadata.BuyerPhone));
            dto.Buyer.Add(new JProperty("email", Metadata.BuyerEmail));

            dto.Token = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)); //No idea what it is useful for
            dto.Guid = Guid.NewGuid().ToString();
            return dto;
        }

        private static string GetPaymentCode(string currency, PaymentMethodId paymentId)
        {
            return PaymentTypes.CHAIN.GetPaymentMethodId(currency) == paymentId ? currency : paymentId.ToString();
        }
#nullable enable
        internal bool Support(PaymentMethodId paymentMethodId)
        {
            var rates = GetPaymentPrompts();
            return rates.TryGet(paymentMethodId) != null;
        }

        public PaymentPrompt? GetPaymentPrompt(PaymentMethodId paymentMethodId)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (PaymentPrompts is null)
                return null;
            var pm = PaymentPrompts[paymentMethodId.ToString()];
#pragma warning restore CS0618 // Type or member is obsolete
            if (pm is null or JToken { Type: JTokenType.Null })
                return null;
            var r = pm.ToObject<PaymentPrompt>(InvoiceDataExtensions.DefaultSerializer)!;
            r.ParentEntity = this;
            r.PaymentMethodId = paymentMethodId;
            return r;
        }
        public PaymentPromptDictionary GetPaymentPrompts()
        {
            PaymentPromptDictionary paymentMethods = new PaymentPromptDictionary();
#pragma warning disable CS0618
            if (PaymentPrompts != null)
            {
                foreach (var prop in PaymentPrompts.Properties())
                {
                    if (!PaymentMethodId.TryParse(prop.Name, out var paymentMethodId))
                    {
                        continue;
                    }
                    if (prop.Value?.Type is not JTokenType.Object)
                    {
                        continue;
                    }
                    var r = prop.Value.ToObject<PaymentPrompt>(InvoiceDataExtensions.DefaultSerializer)!;
                    r.ParentEntity = this;
                    r.PaymentMethodId = paymentMethodId;
                    paymentMethods.Add(r);
                }
            }
#pragma warning restore CS0618
            return paymentMethods;
        }

        public void SetPaymentPrompt(PaymentMethodId paymentMethodId, PaymentPrompt paymentMethod)
        {
            var dict = GetPaymentPrompts();
            paymentMethod.PaymentMethodId = paymentMethodId;
            paymentMethod.ParentEntity = this;
            dict.AddOrReplace(paymentMethod);
            SetPaymentPrompts(dict);
        }

        public void SetPaymentPrompts(PaymentPromptDictionary paymentMethods)
        {
            var obj = new JObject();
#pragma warning disable CS0618
            foreach (var v in paymentMethods)
            {
                obj.Add(new JProperty(v.PaymentMethodId.ToString(), JToken.FromObject(v, InvoiceDataExtensions.DefaultSerializer)));
            }
            PaymentPrompts = obj;
            foreach (var cryptoData in paymentMethods)
            {
                cryptoData.ParentEntity = this;
            }
#pragma warning restore CS0618
            UpdateTotals();
        }
#nullable restore
        public InvoiceState GetInvoiceState()
        {
            return new InvoiceState(Status, ExceptionStatus);
        }

        public bool IsUnsetTopUp()
        {
            return Type == InvoiceType.TopUp && Price == 0.0m;
        }
        [JsonIgnore]
        public Amounts PaidAmount { get; set; }

        /// <summary>
        /// Same as <see cref="Amounts.Net"/> of <see cref="PaidAmount"/>, but only counting payments in 'Settled' state
        /// </summary>
        [JsonIgnore]
        public decimal NetSettled { get; private set; }
        [JsonIgnore]
        public bool DisableAccounting { get; set; }
    }

    public enum InvoiceStatusLegacy
    {
    }
    public static class InvoiceStatusLegacyExtensions
    {
        public static string ToLegacyStatusString(this InvoiceStatus status) =>
            status switch
            {
                InvoiceStatus.Settled => "complete",
                InvoiceStatus.Expired => "expired",
                InvoiceStatus.Invalid => "invalid",
                InvoiceStatus.Processing => "paid",
                InvoiceStatus.New => "new",
                _ => throw new NotSupportedException(status.ToString())
            };
        public static string ToLegacyExceptionStatusString(this InvoiceExceptionStatus status) =>
            status switch
            {
                InvoiceExceptionStatus.None => string.Empty,
                InvoiceExceptionStatus.PaidLate => "paidLater",
                InvoiceExceptionStatus.PaidPartial => "paidPartial",
                InvoiceExceptionStatus.PaidOver => "paidOver",
                InvoiceExceptionStatus.Marked => "marked",
                _ => throw new NotSupportedException(status.ToString())
            };
    }
    public record InvoiceState(InvoiceStatus Status, InvoiceExceptionStatus ExceptionStatus)
    {
        public InvoiceState(string status, string exceptionStatus) :
            this(Enum.Parse<InvoiceStatus>(status), exceptionStatus switch { "None" or "" or null => InvoiceExceptionStatus.None, _ => Enum.Parse<InvoiceExceptionStatus>(exceptionStatus) })
        {
        }

        public bool CanMarkComplete() => (Status, ExceptionStatus) is
        {
            Status: InvoiceStatus.New or InvoiceStatus.Processing or InvoiceStatus.Expired or InvoiceStatus.Invalid
        }
        or
        {
            Status: not InvoiceStatus.Settled,
            ExceptionStatus: InvoiceExceptionStatus.Marked
        };

        public bool CanMarkInvalid() => (Status, ExceptionStatus) is
        {
            Status: InvoiceStatus.New or InvoiceStatus.Processing or InvoiceStatus.Expired
        }
        or
        {
            Status: not InvoiceStatus.Invalid,
            ExceptionStatus: InvoiceExceptionStatus.Marked
        };

        public bool CanRefund() => (Status, ExceptionStatus) is
        {
            Status: InvoiceStatus.Settled or InvoiceStatus.Invalid
        }
        or
        {
            Status: InvoiceStatus.Expired,
            ExceptionStatus: InvoiceExceptionStatus.PaidLate or InvoiceExceptionStatus.PaidOver or InvoiceExceptionStatus.PaidPartial
        };

        public override string ToString()
        {
            return Status + ExceptionStatus switch
            {
                InvoiceExceptionStatus.PaidOver => " (paid over)",
                InvoiceExceptionStatus.PaidLate => " (paid late)",
                InvoiceExceptionStatus.PaidPartial => " (paid partial)",
                InvoiceExceptionStatus.Marked => " (marked)",
                _ => ""
            };
        }
    }

    public class PaymentMethodAccounting
    {
        /// <summary>Total amount of this invoice</summary>
        public decimal TotalDue { get; set; }

        /// <summary>Amount of crypto remaining to pay this invoice</summary>
        public decimal Due { get; set; }

        /// <summary>Same as Due, can be negative</summary>
        public decimal DueUncapped { get; set; }

        /// <summary>If DueUncapped is negative, that means user overpaid invoice</summary>
        public decimal OverpaidHelper
        {
            get { return DueUncapped > 0.0m ? 0.0m : -DueUncapped; }
        }

        /// <summary>
        /// Total amount of the invoice paid after conversion to this crypto currency
        /// </summary>
        public decimal Paid { get; set; }

        /// <summary>
        /// Total amount of the invoice paid in this currency
        /// </summary>
        public decimal PaymentMethodPaid { get; set; }

        /// <summary>
        /// Number of transactions required to pay
        /// </summary>
        public int TxRequired { get; set; }

        /// <summary>
        /// Number of transactions using this payment method
        /// </summary>
        public int TxCount { get; set; }
        /// <summary>
        /// Amount of fee already paid + to be paid in the invoice's currency
        /// </summary>
        public decimal PaymentMethodFee { get; set; }
        /// <summary>
        /// Minimum required to be paid in order to accept invoice as paid
        /// </summary>
        public decimal MinimumTotalDue { get; set; }
    }

    public class PaymentPrompt
    {
        [JsonIgnore]
        public bool Activated => !Inactive;
        public bool Inactive { get; set; }
        [JsonIgnore]
        public InvoiceEntity ParentEntity { get; set; }
        [JsonIgnore]
        public PaymentMethodId PaymentMethodId { get; set; }
        public string Currency { get; set; }
        [JsonIgnore]
        public decimal Rate => Currency is null ? throw new InvalidOperationException("Currency of the payment prompt isn't set") : ParentEntity.GetInvoiceRate(Currency);
        /// <summary>
        /// The maximum divisibility supported by the underlying payment method
        /// </summary>
        public int Divisibility { get; set; }
        /// <summary>
        /// The divisibility to use when calculating the amount to pay.
        /// If null, it will use the <see cref="Divisibility"/>.
        /// </summary>
        public int? RateDivisibility { get; set; }
        /// <summary>
        /// Total additional fee imposed by this specific payment method.
        /// It includes the <see cref="TweakFee"/>.
        /// </summary>
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal PaymentMethodFee { get; set; }
        /// <summary>
        /// An additional fee, hidden from UI, meant to be used when a payment method has a service provider which
        /// have a different way of converting the invoice's amount into the currency of the payment method.
        /// This fee can avoid under/over payments when this case happens.
        /// 
        /// You need to increment it with <see cref="AddTweakFee(decimal)"/> so that the tweak fee is also added to the <see cref="PaymentMethodFee"/>.
        /// </summary>
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal TweakFee { get; set; }
        /// <summary>
        /// A fee, hidden from UI, meant to be used when a payment method has a service provider which
        /// have a different way of converting the invoice's amount into the currency of the payment method.
        /// This fee can avoid under/over payments when this case happens.
        /// </summary>
        /// <param name="value"></param>
        public void AddTweakFee(decimal value)
        {
            TweakFee += value;
            PaymentMethodFee += value;
        }
        public string Destination { get; set; }
        public JToken Details { get; set; }

        public PaymentMethodAccounting Calculate()
        {
            var i = ParentEntity;
            var accounting = new PaymentMethodAccounting();
            var thisPaymentMethodPayments = i.GetPayments(true).Where(p => PaymentMethodId == p.PaymentMethodId).ToList();
            accounting.TxCount = thisPaymentMethodPayments.Count;
            accounting.TxRequired = accounting.TxCount;
            var grossDue = i.Price + i.PaidFee;
            var rate = Rate;
            var divisibility = RateDivisibility ?? Divisibility;
            if (i.MinimumNetDue > 0.0m)
            {
                accounting.TxRequired++;
                grossDue += rate * PaymentMethodFee;
            }
            accounting.TotalDue = Coins(grossDue / rate, divisibility);
            accounting.Paid = Coins(i.PaidAmount.Gross / rate, divisibility);
            accounting.PaymentMethodPaid = Coins(thisPaymentMethodPayments.Sum(p => p.PaidAmount.Gross), divisibility);

            // This one deal with the fact where it might looks like a slight over payment due to the dust of another payment method.
            // So if we detect the NetDue is zero, just cap dueUncapped to 0
            var dueUncapped = i.NetDue == 0.0m ? 0.0m : grossDue - i.PaidAmount.Gross;
            accounting.DueUncapped = Coins(dueUncapped / rate, divisibility);
            accounting.Due = Max(accounting.DueUncapped, 0.0m);

            accounting.PaymentMethodFee = Coins((grossDue - i.Price) / rate, divisibility);

            accounting.MinimumTotalDue = Max(Smallest(divisibility), Coins((grossDue * (1.0m - ((decimal)i.PaymentTolerance / 100.0m))) / rate, divisibility));
            return accounting;
        }

        private decimal Smallest(int precision)
        {
            decimal a = 1.0m;
            for (int i = 0; i < precision; i++)
            {
                a /= 10.0m;
            }
            return a;
        }

        decimal Max(decimal a, decimal b) => a > b ? a : b;

        const decimal MaxCoinValue = decimal.MaxValue / 1_0000_0000m;
        internal static decimal Coins(decimal v, int precision)
        {
            v = Extensions.RoundUp(v, precision);
            // Clamp the value to not crash on degenerate invoices
            if (v > MaxCoinValue)
                v = MaxCoinValue;
            return v;
        }
    }

    public class PaymentEntity : PaymentBlob
    {
        [JsonIgnore]
        public DateTimeOffset ReceivedTime
        {
            get;
            set;
        }
        [JsonIgnore]
        public PaymentStatus Status { get; set; }
        [JsonIgnore]
        public bool Accounted => Status is PaymentStatus.Settled or PaymentStatus.Processing;

        [JsonIgnore]
        public string Currency
        {
            get;
            set;
        }
        [JsonIgnore]
        public PaymentMethodId PaymentMethodId { get; set; }
        [JsonIgnore]
        public decimal Rate { get; set; }
        [JsonIgnore]
        /// <summary>
        public string InvoiceCurrency => InvoiceEntity.Currency;
        /// The amount paid by this payment in the <see cref="Currency"/>
        /// </summary>
        [JsonIgnore]
        public Amounts PaidAmount { get; set; }
        /// <summary>
        /// The amount paid by this payment in the <see cref="InvoiceCurrency"/>
        /// </summary>
        [JsonIgnore]
        public Amounts InvoicePaidAmount { get; set; }
        [JsonIgnore]
        public InvoiceEntity InvoiceEntity { get; set; }
        [JsonIgnore]
        public decimal Value { get; set; }
        [JsonIgnore]
        public string Id { get; set; }

        public void UpdateAmounts()
        {
            var value = Value;
            PaidAmount = new Amounts()
            {
                Currency = Currency,
                Gross = Value,
                Net = Value - PaymentMethodFee
            };
            InvoicePaidAmount = new Amounts()
            {
                Currency = InvoiceCurrency,
                Gross = PaidAmount.Gross * Rate,
                Net = PaidAmount.Net * Rate
            };
        }
    }
}
