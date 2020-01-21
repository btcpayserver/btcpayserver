using NBitcoin;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Newtonsoft.Json.Linq;
using NBitcoin.DataEncoders;
using BTCPayServer.Data;
using NBXplorer.Models;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using BTCPayServer.Payments;
using NBitpayClient;
using BTCPayServer.Payments.Bitcoin;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Services.Invoices
{
    public class BuyerInformation
    {
        [JsonProperty(PropertyName = "buyerName")]
        public string BuyerName
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerEmail")]
        public string BuyerEmail
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerCountry")]
        public string BuyerCountry
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerZip")]
        public string BuyerZip
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerState")]
        public string BuyerState
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerCity")]
        public string BuyerCity
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerAddress2")]
        public string BuyerAddress2
        {
            get; set;
        }
        [JsonProperty(PropertyName = "buyerAddress1")]
        public string BuyerAddress1
        {
            get; set;
        }

        [JsonProperty(PropertyName = "buyerPhone")]
        public string BuyerPhone
        {
            get; set;
        }
    }

    public class ProductInformation
    {
        [JsonProperty(PropertyName = "itemDesc")]
        public string ItemDesc
        {
            get; set;
        }
        [JsonProperty(PropertyName = "itemCode")]
        public string ItemCode
        {
            get; set;
        }
        [JsonProperty(PropertyName = "physical")]
        public bool Physical
        {
            get; set;
        }

        [JsonProperty(PropertyName = "price")]
        public decimal Price
        {
            get; set;
        }

        [JsonProperty(PropertyName = "taxIncluded", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public decimal TaxIncluded
        {
            get; set;
        }

        [JsonProperty(PropertyName = "currency")]
        public string Currency
        {
            get; set;
        }
    }
    public class InvoiceEntity
    {
        [JsonIgnore]
        public BTCPayNetworkProvider Networks { get; set; }
        public const int InternalTagSupport_Version = 1;
        public const int Lastest_Version = 1;
        public int Version { get; set; }
        public string Id
        {
            get; set;
        }
        public string StoreId
        {
            get; set;
        }

        public string OrderId
        {
            get; set;
        }

        public SpeedPolicy SpeedPolicy
        {
            get; set;
        }
        [Obsolete("Use GetPaymentMethod(network) instead")]
        public decimal Rate
        {
            get; set;
        }
        public DateTimeOffset InvoiceTime
        {
            get; set;
        }
        public DateTimeOffset ExpirationTime
        {
            get; set;
        }

        [Obsolete("Use GetPaymentMethod(network).GetPaymentMethodDetails().GetDestinationAddress() instead")]
        public string DepositAddress
        {
            get; set;
        }
        public ProductInformation ProductInformation
        {
            get; set;
        }
        public BuyerInformation BuyerInformation
        {
            get; set;
        }
        public string PosData
        {
            get;
            set;
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HashSet<string> InternalTags { get; set; } = new HashSet<string>();

        public string[] GetInternalTags(string suffix)
        {
            return InternalTags == null ? Array.Empty<string>() : InternalTags
                                                  .Where(t => t.StartsWith(suffix, StringComparison.InvariantCulture))
                                                  .Select(t => t.Substring(suffix.Length)).ToArray();
        }
        
        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategy
        {
            get;
            set;
        }

        [Obsolete("Use GetPaymentMethodFactories() instead")]
        public string DerivationStrategies
        {
            get;
            set;
        }
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
                    var paymentMethodId = PaymentMethodId.Parse(strat.Name);
                    var network = Networks.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
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
        public InvoiceStatus Status
        {
            get;
            set;
        }
        [JsonProperty(PropertyName = "status")]
        [Obsolete("Use Status instead")]
        public string StatusString => InvoiceState.ToString(Status);
        [JsonIgnore]
        public InvoiceExceptionStatus ExceptionStatus
        {
            get; set;
        }
        [JsonProperty(PropertyName = "exceptionStatus")]
        [Obsolete("Use ExceptionStatus instead")]
        public string ExceptionStatusString => InvoiceState.ToString(ExceptionStatus);

        [Obsolete("Use GetPayments instead")]
        public List<PaymentEntity> Payments
        {
            get; set;
        }

#pragma warning disable CS0618
        public List<PaymentEntity> GetPayments()
        {
            return Payments?.ToList() ?? new List<PaymentEntity>();
        }
        public List<PaymentEntity> GetPayments(string cryptoCode)
        {
            return Payments.Where(p => p.CryptoCode == cryptoCode).ToList();
        }
        public List<PaymentEntity> GetPayments(BTCPayNetworkBase network)
        {
            return GetPayments(network.CryptoCode);
        }
#pragma warning restore CS0618
        public bool Refundable
        {
            get;
            set;
        }
        public string RefundMail
        {
            get;
            set;
        }
        [JsonProperty("redirectURL")]
        public string RedirectURLTemplate
        {
            get;
            set;
        }

        [JsonIgnore]
        public Uri RedirectURL => FillPlaceholdersUri(RedirectURLTemplate);

        private Uri FillPlaceholdersUri(string v)
        {
            var uriStr = (v ?? string.Empty).Replace("{OrderId}", OrderId ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{InvoiceId}", Id ?? "", StringComparison.OrdinalIgnoreCase);
            if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                return uri;
            return null;
        }

        public bool RedirectAutomatically
        {
            get;
            set;
        }

        [Obsolete("Use GetPaymentMethod(network).GetTxFee() instead")]
        public Money TxFee
        {
            get;
            set;
        }
        public bool FullNotifications
        {
            get;
            set;
        }
        public string NotificationEmail
        {
            get;
            set;
        }

        [JsonProperty("notificationURL")]
        public string NotificationURLTemplate
        {
            get;
            set;
        }

        [JsonIgnore]
        public Uri NotificationURL => FillPlaceholdersUri(NotificationURLTemplate);
        public string ServerUrl
        {
            get;
            set;
        }

        [Obsolete("Use Set/GetPaymentMethod() instead")]
        [JsonProperty(PropertyName = "cryptoData")]
        public JObject PaymentMethod { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public DateTimeOffset MonitoringExpiration
        {
            get;
            set;
        }
        public HistoricalAddressInvoiceData[] HistoricalAddresses
        {
            get;
            set;
        }

        public HashSet<string> AvailableAddressHashes
        {
            get;
            set;
        }
        public bool ExtendedNotifications { get; set; }
        public List<InvoiceEventData> Events { get; internal set; }
        public double PaymentTolerance { get; set; }

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
                OrderId = OrderId,
                PosData = PosData,
                CurrentTime = DateTimeOffset.UtcNow,
                InvoiceTime = InvoiceTime,
                ExpirationTime = ExpirationTime,
#pragma warning disable CS0618 // Type or member is obsolete
                Status = StatusString,
                ExceptionStatus = ExceptionStatus == InvoiceExceptionStatus.None ? new JValue(false) : new JValue(ExceptionStatusString),
#pragma warning restore CS0618 // Type or member is obsolete
                Currency = ProductInformation.Currency,
                Flags = new Flags() { Refundable = Refundable },
                PaymentSubtotals = new Dictionary<string, decimal>(),
                PaymentTotals = new Dictionary<string, decimal>(),
                SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>(),
                Addresses = new Dictionary<string, string>(),
                PaymentCodes = new Dictionary<string, InvoicePaymentUrls>(),
                ExchangeRates = new Dictionary<string, Dictionary<string, decimal>>()
            };

            dto.Url = ServerUrl.WithTrailingSlash() + $"invoice?id=" + Id;
            dto.CryptoInfo = new List<NBitpayClient.InvoiceCryptoInfo>();
            dto.MinerFees = new Dictionary<string, MinerFeeInfo>();
            foreach (var info in this.GetPaymentMethods())
            {
                var accounting = info.Calculate();
                var cryptoInfo = new NBitpayClient.InvoiceCryptoInfo();
                var subtotalPrice = accounting.TotalDue - accounting.NetworkFee;
                var cryptoCode = info.GetId().CryptoCode;
                var address = info.GetPaymentMethodDetails()?.GetPaymentDestination();
                var exrates = new Dictionary<string, decimal>
                {
                    { ProductInformation.Currency, cryptoInfo.Rate }
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

                cryptoInfo.Payments = GetPayments(info.Network).Select(entity =>
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
                

                if (paymentId.PaymentType == PaymentTypes.LightningLike)
                {
                    cryptoInfo.PaymentUrls = new InvoicePaymentUrls()
                    {
                        BOLT11 = $"lightning:{cryptoInfo.Address}"
                    };
                }
                else if (paymentId.PaymentType == PaymentTypes.BTCLike)
                {
                    var minerInfo = new MinerFeeInfo();
                    minerInfo.TotalFee = accounting.NetworkFee.Satoshi;
                    minerInfo.SatoshiPerBytes = ((BitcoinLikeOnChainPaymentMethod)info.GetPaymentMethodDetails()).FeeRate
                        .GetFee(1).Satoshi;
                    dto.MinerFees.TryAdd(cryptoInfo.CryptoCode, minerInfo);
                    cryptoInfo.PaymentUrls = new NBitpayClient.InvoicePaymentUrls()
                    {
                        BIP21 = ((BTCPayNetwork)info.Network).GenerateBIP21(cryptoInfo.Address, cryptoInfo.Due),
                    };

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

            Populate(ProductInformation, dto);
            dto.Buyer = new JObject();
            dto.Buyer.Add(new JProperty("name", BuyerInformation.BuyerName));
            dto.Buyer.Add(new JProperty("address1", BuyerInformation.BuyerAddress1));
            dto.Buyer.Add(new JProperty("address2", BuyerInformation.BuyerAddress2));
            dto.Buyer.Add(new JProperty("locality", BuyerInformation.BuyerCity));
            dto.Buyer.Add(new JProperty("region", BuyerInformation.BuyerState));
            dto.Buyer.Add(new JProperty("postalCode", BuyerInformation.BuyerZip));
            dto.Buyer.Add(new JProperty("country", BuyerInformation.BuyerCountry));
            dto.Buyer.Add(new JProperty("phone", BuyerInformation.BuyerPhone));
            dto.Buyer.Add(new JProperty("email", string.IsNullOrWhiteSpace(BuyerInformation.BuyerEmail) ? RefundMail : BuyerInformation.BuyerEmail));

            dto.Token = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)); //No idea what it is useful for
            dto.Guid = Guid.NewGuid().ToString();
            return dto;
        }

        private void Populate<TFrom, TDest>(TFrom from, TDest dest)
        {
            var str = JsonConvert.SerializeObject(from);
            JsonConvert.PopulateObject(str, dest);
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
                    var paymentMethodId = PaymentMethodId.Parse(prop.Name);
                    r.CryptoCode = paymentMethodId.CryptoCode;
                    r.PaymentType = paymentMethodId.PaymentType.ToString();
                    r.ParentEntity = this;
                    r.Network = Networks?.UnfilteredNetworks.GetNetwork<BTCPayNetworkBase>(r.CryptoCode);
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
    }

    public enum InvoiceStatus
    {
        New,
        Paid,
        Expired,
        Invalid,
        Complete,
        Confirmed
    }
    public enum InvoiceExceptionStatus
    {
        None,
        PaidLate,
        PaidPartial,
        Marked,
        Invalid,
        PaidOver
    }
    public class InvoiceState
    {
        static Dictionary<string, InvoiceStatus> _StringToInvoiceStatus;
        static Dictionary<InvoiceStatus, string> _InvoiceStatusToString;

        static Dictionary<string, InvoiceExceptionStatus> _StringToExceptionStatus;
        static Dictionary<InvoiceExceptionStatus, string> _ExceptionStatusToString;

        static InvoiceState()
        {
            _StringToInvoiceStatus = new Dictionary<string, InvoiceStatus>();
            _StringToInvoiceStatus.Add("paid", InvoiceStatus.Paid);
            _StringToInvoiceStatus.Add("expired", InvoiceStatus.Expired);
            _StringToInvoiceStatus.Add("invalid", InvoiceStatus.Invalid);
            _StringToInvoiceStatus.Add("complete", InvoiceStatus.Complete);
            _StringToInvoiceStatus.Add("new", InvoiceStatus.New);
            _StringToInvoiceStatus.Add("confirmed", InvoiceStatus.Confirmed);
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
        public InvoiceState(InvoiceStatus status, InvoiceExceptionStatus exceptionStatus)
        {
            Status = status;
            ExceptionStatus = exceptionStatus;
        }

        public InvoiceStatus Status { get; }
        public InvoiceExceptionStatus ExceptionStatus { get; }

        public static string ToString(InvoiceStatus status)
        {
            return _InvoiceStatusToString[status];
        }

        public static string ToString(InvoiceExceptionStatus exceptionStatus)
        {
            return _ExceptionStatusToString[exceptionStatus];
        }

        public bool CanMarkComplete()
        {
            return (Status == InvoiceStatus.Paid) ||
#pragma warning disable CA1305 // Specify IFormatProvider
                   ((Status == InvoiceStatus.New || Status == InvoiceStatus.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidPartial) ||
                   ((Status == InvoiceStatus.New || Status == InvoiceStatus.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidLate) ||
                   (Status != InvoiceStatus.Complete && ExceptionStatus == InvoiceExceptionStatus.Marked) ||
                   (Status == InvoiceStatus.Invalid);
#pragma warning restore CA1305 // Specify IFormatProvider
        }

        public bool CanMarkInvalid()
        {
            return (Status == InvoiceStatus.Paid) ||
                   (Status == InvoiceStatus.New) ||
#pragma warning disable CA1305 // Specify IFormatProvider
                   ((Status == InvoiceStatus.New || Status == InvoiceStatus.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidPartial) ||
                   ((Status == InvoiceStatus.New || Status == InvoiceStatus.Expired) && ExceptionStatus == InvoiceExceptionStatus.PaidLate) ||
                   (Status != InvoiceStatus.Invalid && ExceptionStatus == InvoiceExceptionStatus.Marked);
#pragma warning restore CA1305 // Specify IFormatProvider;
        }
        public override string ToString()
        {
            return ToString(Status) + (ExceptionStatus == InvoiceExceptionStatus.None ? string.Empty : $" ({ToString(ExceptionStatus)})");
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
        /// Minimum required to be paid in order to accept invoice as paid
        /// </summary>
        public Money MinimumTotalDue { get; set; }
    }

    public class PaymentMethod
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
                return new Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                {
                    FeeRate = FeeRate,
                    DepositAddress = string.IsNullOrEmpty(DepositAddress) ? null : DepositAddress,
                    NextNetworkFee = NextNetworkFee
                };
            }
            else
            {
                IPaymentMethodDetails details = GetId().PaymentType.DeserializePaymentMethodDetails(PaymentMethodDetails.ToString());
                if (details is Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod btcLike)
                {
                    btcLike.NextNetworkFee = NextNetworkFee;
                    btcLike.DepositAddress = string.IsNullOrEmpty(DepositAddress) ? null : DepositAddress;
                    btcLike.FeeRate = FeeRate;
                }
                return details;
            }
            throw new NotSupportedException(PaymentType);
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
            var jobj = JObject.Parse(JsonConvert.SerializeObject(paymentMethod));
            PaymentMethodDetails = jobj;

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

            var totalDue = ParentEntity.ProductInformation.Price / Rate;
            var paid = 0m;
            var cryptoPaid = 0.0m;

            int precision = Network?.Divisibility ?? 8;
            var totalDueNoNetworkCost = Money.Coins(Extensions.RoundUp(totalDue, precision));
            bool paidEnough = paid >= Extensions.RoundUp(totalDue, precision);
            int txRequired = 0;

            _ = ParentEntity.GetPayments()
                .Where(p => p.Accounted && paymentPredicate(p))
                .OrderBy(p => p.ReceivedTime)
                .Select(_ =>
                {
                    var txFee = _.GetValue(paymentMethods, GetId(), _.NetworkFee, precision);
                    paid += _.GetValue(paymentMethods, GetId(), null,  precision);
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
            var minimumTotalDueSatoshi = Math.Max(1.0m, accounting.TotalDue.Satoshi * (1.0m - ((decimal)ParentEntity.PaymentTolerance / 100.0m)));
            accounting.MinimumTotalDue = Money.Satoshis(minimumTotalDueSatoshi);
            return accounting;
        }

        private decimal GetTxFee()
        {
            var method = GetPaymentMethodDetails();
            if (method == null)
                return 0.0m;
            return method.GetNextNetworkFee();
        }
    }

    public class PaymentEntity
    {
        [NotMapped]
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; }
        public int Version { get; set; }
        public DateTimeOffset ReceivedTime
        {
            get; set;
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
                paymentData = GetPaymentMethodId().PaymentType.DeserializePaymentData(Network,CryptoPaymentData);
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
            CryptoPaymentData = GetPaymentMethodId().PaymentType.SerializePaymentData(Network,cryptoPaymentData);
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
            return new PaymentMethodId(CryptoCode ?? "BTC", string.IsNullOrEmpty(CryptoPaymentDataType) ? PaymentTypes.BTCLike : PaymentTypes.Parse(CryptoPaymentDataType));
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
