using NBitcoin;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
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

        [JsonProperty(PropertyName = "currency")]
        public string Currency
        {
            get; set;
        }
    }

    public enum SpeedPolicy
    {
        HighSpeed = 0,
        MediumSpeed = 1,
        LowSpeed = 2,
        LowMediumSpeed = 3
    }
    public class InvoiceEntity
    {
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
        public IEnumerable<T> GetSupportedPaymentMethod<T>(PaymentMethodId paymentMethodId, BTCPayNetworkProvider networks) where T : ISupportedPaymentMethod
        {
            return
                GetSupportedPaymentMethod(networks)
                .Where(p => paymentMethodId == null || p.PaymentId == paymentMethodId)
                .OfType<T>();
        }
        public IEnumerable<T> GetSupportedPaymentMethod<T>(BTCPayNetworkProvider networks) where T : ISupportedPaymentMethod
        {
            return GetSupportedPaymentMethod<T>(null, networks);
        }
        public IEnumerable<ISupportedPaymentMethod> GetSupportedPaymentMethod(BTCPayNetworkProvider networks)
        {
#pragma warning disable CS0618
            bool btcReturned = false;
            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    var paymentMethodId = PaymentMethodId.Parse(strat.Name);
                    var network = networks.GetNetwork(paymentMethodId.CryptoCode);
                    if (network != null)
                    {
                        if (network == networks.BTC && paymentMethodId.PaymentType == PaymentTypes.BTCLike)
                            btcReturned = true;
                        yield return PaymentMethodExtensions.Deserialize(paymentMethodId, strat.Value, network);
                    }
                }
            }

            if (!btcReturned && !string.IsNullOrEmpty(DerivationStrategy))
            {
                if (networks.BTC != null)
                {
                    yield return BTCPayServer.DerivationStrategy.Parse(DerivationStrategy, networks.BTC);
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
                if (strat.PaymentId.IsBTCOnChain)
                    DerivationStrategy = ((JValue)PaymentMethodExtensions.Serialize(strat)).Value<string>();
            }
            DerivationStrategies = JsonConvert.SerializeObject(obj);
#pragma warning restore CS0618
        }

        public string Status
        {
            get;
            set;
        }
        public string ExceptionStatus
        {
            get; set;
        }

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
        public List<PaymentEntity> GetPayments(BTCPayNetwork network)
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
        public string RedirectURL
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
        public string NotificationURL
        {
            get;
            set;
        }
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


        public InvoiceResponse EntityToDTO(BTCPayNetworkProvider networkProvider)
        {
            ServerUrl = ServerUrl ?? "";
            InvoiceResponse dto = new InvoiceResponse
            {
                Id = Id,
                OrderId = OrderId,
                PosData = PosData,
                CurrentTime = DateTimeOffset.UtcNow,
                InvoiceTime = InvoiceTime,
                ExpirationTime = ExpirationTime,
                Status = Status,
                Currency = ProductInformation.Currency,
                Flags = new Flags() { Refundable = Refundable },
                PaymentSubtotals = new Dictionary<string, long>(),
                PaymentTotals = new Dictionary<string, long>(),
                SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>(),
                Addresses = new Dictionary<string, string>(),
                PaymentCodes = new Dictionary<string, InvoicePaymentUrls>(),
                ExchangeRates = new Dictionary<string, Dictionary<string, decimal>>()
            };

            dto.Url = ServerUrl.WithTrailingSlash() + $"invoice?id=" + Id;
            dto.CryptoInfo = new List<NBitpayClient.InvoiceCryptoInfo>();
            dto.MinerFees = new Dictionary<string, MinerFeeInfo>();
            foreach (var info in this.GetPaymentMethods(networkProvider))
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
                var scheme = info.Network.UriScheme;
                cryptoInfo.Url = ServerUrl.WithTrailingSlash() + $"i/{paymentId}/{Id}";

                if (paymentId.PaymentType == PaymentTypes.BTCLike)
                {
                    var minerInfo = new MinerFeeInfo();
                    minerInfo.TotalFee = accounting.NetworkFee.Satoshi;
                    minerInfo.SatoshiPerBytes = ((BitcoinLikeOnChainPaymentMethod)info.GetPaymentMethodDetails()).FeeRate.GetFee(1).Satoshi;
                    dto.MinerFees.TryAdd(paymentId.CryptoCode, minerInfo);
                    var cryptoSuffix = cryptoInfo.CryptoCode == "BTC" ? "" : "/" + cryptoInfo.CryptoCode;
                    cryptoInfo.PaymentUrls = new NBitpayClient.InvoicePaymentUrls()
                    {
                        BIP72 = $"{scheme}:{cryptoInfo.Address}?amount={cryptoInfo.Due}&r={ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}")}",
                        BIP72b = $"{scheme}:?r={ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}")}",
                        BIP73 = ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}"),
                        BIP21 = $"{scheme}:{cryptoInfo.Address}?amount={cryptoInfo.Due}",
                    };
                }

                if (paymentId.PaymentType == PaymentTypes.LightningLike)
                {
                    cryptoInfo.PaymentUrls = new NBitpayClient.InvoicePaymentUrls()
                    {
                        BOLT11 = $"lightning:{cryptoInfo.Address}"
                    };
                }
#pragma warning disable CS0618
                if (info.CryptoCode == "BTC" && paymentId.PaymentType == PaymentTypes.BTCLike)
                {
                    dto.BTCPrice = cryptoInfo.Price;
                    dto.Rate = cryptoInfo.Rate;
                    dto.ExRates = cryptoInfo.ExRates;
                    dto.BitcoinAddress = cryptoInfo.Address;
                    dto.BTCPaid = cryptoInfo.Paid;
                    dto.BTCDue = cryptoInfo.Due;
                    dto.PaymentUrls = cryptoInfo.PaymentUrls;
                }

#pragma warning restore CS0618
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
            dto.ExceptionStatus = ExceptionStatus == null ? new JValue(false) : new JValue(ExceptionStatus);
            return dto;
        }

        private void Populate<TFrom, TDest>(TFrom from, TDest dest)
        {
            var str = JsonConvert.SerializeObject(from);
            JsonConvert.PopulateObject(str, dest);
        }

        internal bool Support(PaymentMethodId paymentMethodId)
        {
            var rates = GetPaymentMethods(null);
            return rates.TryGet(paymentMethodId) != null;
        }

        public PaymentMethod GetPaymentMethod(PaymentMethodId paymentMethodId, BTCPayNetworkProvider networkProvider)
        {
            GetPaymentMethods(networkProvider).TryGetValue(paymentMethodId, out var data);
            return data;
        }
        public PaymentMethod GetPaymentMethod(BTCPayNetwork network, PaymentTypes paymentType, BTCPayNetworkProvider networkProvider)
        {
            return GetPaymentMethod(new PaymentMethodId(network.CryptoCode, paymentType), networkProvider);
        }

        public PaymentMethodDictionary GetPaymentMethods(BTCPayNetworkProvider networkProvider)
        {
            PaymentMethodDictionary rates = new PaymentMethodDictionary(networkProvider);
            var serializer = new Serializer(Dummy);
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
                    r.Network = networkProvider?.GetNetwork(r.CryptoCode);
                    if (r.Network != null || networkProvider == null)
                        rates.Add(r);
                }
            }
#pragma warning restore CS0618
            return rates;
        }

        Network Dummy = Network.Main;

        public void SetPaymentMethod(PaymentMethod paymentMethod)
        {
            var dict = GetPaymentMethods(null);
            dict.AddOrReplace(paymentMethod);
            SetPaymentMethods(dict);
        }

        public void SetPaymentMethods(PaymentMethodDictionary paymentMethods)
        {
            if (paymentMethods.NetworkProvider != null)
                throw new InvalidOperationException($"{nameof(paymentMethods)} should have NetworkProvider to null");
            var obj = new JObject();
            var serializer = new Serializer(Dummy);
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
    }

    public class PaymentMethodAccounting
    {
        /// <summary>
        /// Total amount of this invoice
        /// </summary>
        public Money TotalDue { get; set; }

        /// <summary>
        /// Amount of crypto remaining to pay this invoice
        /// </summary>
        public Money Due { get; set; }

        /// <summary>
        /// Same as Due, can be negative
        /// </summary>
        public Money DueUncapped { get; set; }
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
        /// Minimum required to be paid in order to accept invocie as paid
        /// </summary>
        public Money MinimumTotalDue { get; set; }
    }

    public class PaymentMethod
    {
        [JsonIgnore]
        public InvoiceEntity ParentEntity { get; set; }
        [JsonIgnore]
        public BTCPayNetwork Network { get; set; }
        [JsonProperty(PropertyName = "cryptoCode", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [Obsolete("Use GetId().CryptoCode instead")]
        public string CryptoCode { get; set; }
        [JsonProperty(PropertyName = "paymentType", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [Obsolete("Use GetId().PaymentType instead")]
        public string PaymentType { get; set; }


        public PaymentMethodId GetId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new PaymentMethodId(CryptoCode, string.IsNullOrEmpty(PaymentType) ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(PaymentType));
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
                    TxFee = TxFee
                };
            }
            else
            {
                var details = PaymentMethodExtensions.DeserializePaymentMethodDetails(GetId(), PaymentMethodDetails);
                if (details is Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod btcLike)
                {
                    btcLike.TxFee = TxFee;
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
                TxFee = bitcoinPaymentMethod.TxFee;
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
        [Obsolete("Use ((BitcoinLikeOnChainPaymentMethod)GetPaymentMethod()).TxFee")]
        public Money TxFee { get; set; }
        [JsonProperty(PropertyName = "depositAddress")]
        [Obsolete("Use ((BitcoinLikeOnChainPaymentMethod)GetPaymentMethod()).DepositAddress")]
        public string DepositAddress { get; set; }

        public PaymentMethodAccounting Calculate(Func<PaymentEntity, bool> paymentPredicate = null)
        {
            paymentPredicate = paymentPredicate ?? new Func<PaymentEntity, bool>((p) => true);
            var paymentMethods = ParentEntity.GetPaymentMethods(null);

            var totalDue = ParentEntity.ProductInformation.Price / Rate;
            var paid = 0m;
            var cryptoPaid = 0.0m;

            int precision = 8;
            var totalDueNoNetworkCost = Money.Coins(Extensions.RoundUp(totalDue, precision));
            bool paidEnough = paid >= Extensions.RoundUp(totalDue, precision);
            int txRequired = 0;
            var payments =
                ParentEntity.GetPayments()
                .Where(p => p.Accounted && paymentPredicate(p))
                .OrderBy(p => p.ReceivedTime)
                .Select(_ =>
                {
                    var txFee = _.GetValue(paymentMethods, GetId(), paymentMethods[_.GetPaymentMethodId()].GetTxFee());
                    paid += _.GetValue(paymentMethods, GetId());
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
                })
                .ToArray();

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
            return method.GetTxFee();
        }
    }

    public class PaymentEntity
    {
        public DateTimeOffset ReceivedTime
        {
            get; set;
        }

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
#pragma warning disable CS0618
            if (string.IsNullOrEmpty(CryptoPaymentDataType))
            {
                // In case this is a payment done before this update, consider it unconfirmed with RBF for safety
                var paymentData = new Payments.Bitcoin.BitcoinLikePaymentData();
                paymentData.Outpoint = Outpoint;
                paymentData.Output = Output;
                paymentData.RBF = true;
                paymentData.ConfirmationCount = 0;
                paymentData.Legacy = true;
                return paymentData;
            }
            if (GetPaymentMethodId().PaymentType == PaymentTypes.BTCLike)
            {
                var paymentData = JsonConvert.DeserializeObject<Payments.Bitcoin.BitcoinLikePaymentData>(CryptoPaymentData);
                // legacy
                paymentData.Output = Output;
                paymentData.Outpoint = Outpoint;
                return paymentData;
            }
            if (GetPaymentMethodId().PaymentType == PaymentTypes.LightningLike)
            {
                return JsonConvert.DeserializeObject<Payments.Lightning.LightningLikePaymentData>(CryptoPaymentData);
            }

            throw new NotSupportedException(nameof(CryptoPaymentDataType) + " does not support " + CryptoPaymentDataType);
#pragma warning restore CS0618
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
            CryptoPaymentData = JsonConvert.SerializeObject(cryptoPaymentData);
#pragma warning restore CS0618
            return this;
        }
        internal decimal GetValue(PaymentMethodDictionary paymentMethods, PaymentMethodId paymentMethodId, decimal? value = null)
        {
            value = value ?? this.GetCryptoPaymentData().GetValue();
            var to = paymentMethodId;
            var from = this.GetPaymentMethodId();
            if (to == from)
                return decimal.Round(value.Value, 8);
            var fromRate = paymentMethods[from].Rate;
            var toRate = paymentMethods[to].Rate;

            var fiatValue = fromRate * decimal.Round(value.Value, 8);
            var otherCurrencyValue = toRate == 0 ? 0.0m : fiatValue / toRate;
            return otherCurrencyValue;
        }

        public PaymentMethodId GetPaymentMethodId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new PaymentMethodId(CryptoCode ?? "BTC", string.IsNullOrEmpty(CryptoPaymentDataType) ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(CryptoPaymentDataType));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public string GetCryptoCode()
        {
#pragma warning disable CS0618
            return CryptoCode ?? "BTC";
#pragma warning restore CS0618
        }
    }

    public interface CryptoPaymentData
    {
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
        bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network);
        bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network);

        PaymentTypes GetPaymentType();
    }
}
