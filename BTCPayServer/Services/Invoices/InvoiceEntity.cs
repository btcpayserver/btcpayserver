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
        LowSpeed = 2
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
        [Obsolete("Use GetCryptoData(network).Rate instead")]
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

        [Obsolete("Use GetCryptoData(network).DepositAddress instead")]
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

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategies
        {
            get;
            set;
        }

        public DerivationStrategyBase GetDerivationStrategy(BTCPayNetwork network)
        {
#pragma warning disable CS0618
            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
#pragma warning restore CS0618
                foreach (var strat in strategies.Properties())
                {
                    if (strat.Name == network.CryptoCode)
                    {
                        return BTCPayServer.DerivationStrategy.Parse(strat.Value.Value<string>(), network).DerivationStrategyBase;
                    }
                }
            }
#pragma warning disable CS0618
            if (network.IsBTC && !string.IsNullOrEmpty(DerivationStrategy))
            {
                return BTCPayServer.DerivationStrategy.Parse(DerivationStrategy, network).DerivationStrategyBase;
            }
            return null;
#pragma warning restore CS0618
        }

        public IEnumerable<DerivationStrategy> GetDerivationStrategies(BTCPayNetworkProvider networks)
        {
#pragma warning disable CS0618
            bool btcReturned = false;
            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    var network = networks.GetNetwork(strat.Name);
                    if (network != null)
                    {
                        if (network == networks.BTC)
                            btcReturned = true;
                        yield return BTCPayServer.DerivationStrategy.Parse(strat.Value.Value<string>(), network);
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

        internal void SetDerivationStrategies(IEnumerable<DerivationStrategy> derivationStrategies)
        {
            JObject obj = new JObject();
            foreach (var strat in derivationStrategies)
            {
                obj.Add(strat.Network.CryptoCode, new JValue(strat.DerivationStrategyBase.ToString()));
#pragma warning disable CS0618
                if (strat.Network.IsBTC)
                    DerivationStrategy = strat.DerivationStrategyBase.ToString();
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
            return Payments.ToList();
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

        [Obsolete("Use GetCryptoData(network).TxFee instead")]
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

        [Obsolete("Use Set/GetCryptoData() instead")]
        public JObject CryptoData { get; set; }

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
                Flags = new Flags() { Refundable = Refundable }
            };

            dto.CryptoInfo = new List<NBitpayClient.InvoiceCryptoInfo>();
            foreach (var info in this.GetCryptoData(networkProvider, true))
            {
                var accounting = info.Calculate();
                var cryptoInfo = new NBitpayClient.InvoiceCryptoInfo();
                cryptoInfo.CryptoCode = info.GetId().CryptoCode;
                cryptoInfo.PaymentType = info.GetId().PaymentType.ToString();
                cryptoInfo.Rate = info.Rate;
                cryptoInfo.Price = Money.Coins(ProductInformation.Price / cryptoInfo.Rate).ToString();

                cryptoInfo.Due = accounting.Due.ToString();
                cryptoInfo.Paid = accounting.Paid.ToString();
                cryptoInfo.TotalDue = accounting.TotalDue.ToString();
                cryptoInfo.NetworkFee = accounting.NetworkFee.ToString();
                cryptoInfo.TxCount = accounting.TxCount;
                cryptoInfo.CryptoPaid = accounting.CryptoPaid.ToString();

                if (info.GetPaymentMethod() is BitcoinLikeOnChainPaymentMethod onchainMethod)
                    cryptoInfo.Address = onchainMethod.DepositAddress?.ToString();
                cryptoInfo.ExRates = new Dictionary<string, double>
                {
                    { ProductInformation.Currency, (double)cryptoInfo.Rate }
                };

                var scheme = info.Network.UriScheme;
                var cryptoSuffix = cryptoInfo.CryptoCode == "BTC" ? "" : "/" + cryptoInfo.CryptoCode;
                cryptoInfo.Url = ServerUrl.WithTrailingSlash() + $"invoice{cryptoSuffix}?id=" + Id;


                cryptoInfo.PaymentUrls = new NBitpayClient.InvoicePaymentUrls()
                {
                    BIP72 = $"{scheme}:{cryptoInfo.Address}?amount={cryptoInfo.Due}&r={ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}")}",
                    BIP72b = $"{scheme}:?r={ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}")}",
                    BIP73 = ServerUrl.WithTrailingSlash() + ($"i/{Id}{cryptoSuffix}"),
                    BIP21 = $"{scheme}:{cryptoInfo.Address}?amount={cryptoInfo.Due}",
                };
#pragma warning disable CS0618
                if (info.CryptoCode == "BTC")
                {
                    dto.Url = cryptoInfo.Url;
                    dto.BTCPrice = cryptoInfo.Price;
                    dto.Rate = cryptoInfo.Rate;
                    dto.ExRates = cryptoInfo.ExRates;
                    dto.BitcoinAddress = cryptoInfo.Address;
                    dto.BTCPaid = cryptoInfo.Paid;
                    dto.BTCDue = cryptoInfo.Due;
                    dto.PaymentUrls = cryptoInfo.PaymentUrls;
                }
#pragma warning restore CS0618
                if (!info.IsPhantomBTC)
                    dto.CryptoInfo.Add(cryptoInfo);
            }

            Populate(ProductInformation, dto);
            Populate(BuyerInformation, dto);

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

        internal bool Support(CryptoDataId cryptoDataId)
        {
            var rates = GetCryptoData(null);
            return rates.TryGet(cryptoDataId) != null;
        }

        public CryptoData GetCryptoData(CryptoDataId cryptoDataId, BTCPayNetworkProvider networkProvider)
        {
            GetCryptoData(networkProvider).TryGetValue(cryptoDataId, out var data);
            return data;
        }
        public CryptoData GetCryptoData(BTCPayNetwork network, PaymentTypes paymentType, BTCPayNetworkProvider networkProvider)
        {
            return GetCryptoData(new CryptoDataId(network.CryptoCode, paymentType), networkProvider);
        }

        public CryptoDataDictionary GetCryptoData(BTCPayNetworkProvider networkProvider, bool alwaysIncludeBTC = false)
        {
            CryptoDataDictionary rates = new CryptoDataDictionary();
            var serializer = new Serializer(Dummy);
            CryptoData phantom = null;
#pragma warning disable CS0618
            // Legacy
            if (alwaysIncludeBTC)
            {
                var btcNetwork = networkProvider?.GetNetwork("BTC");
                phantom = new CryptoData() { ParentEntity = this, IsPhantomBTC = true, Rate = Rate, CryptoCode = "BTC", TxFee = TxFee, FeeRate = new FeeRate(TxFee, 100), DepositAddress = DepositAddress, Network = btcNetwork };
                rates.Add(phantom);
            }
            if (CryptoData != null)
            {
                foreach (var prop in CryptoData.Properties())
                {
                    if (prop.Name == "BTC" && phantom != null)
                        rates.Remove(phantom);
                    var r = serializer.ToObject<CryptoData>(prop.Value.ToString());
                    var cryptoDataId = CryptoDataId.Parse(prop.Name);
                    r.CryptoCode = cryptoDataId.CryptoCode;
                    r.PaymentType = cryptoDataId.PaymentType.ToString();
                    r.ParentEntity = this;
                    r.Network = networkProvider?.GetNetwork(r.CryptoCode);
                    rates.Add(r);
                }
            }
#pragma warning restore CS0618
            return rates;
        }

        Network Dummy = Network.Main;

        public void SetCryptoData(CryptoData cryptoData)
        {
            var dict = GetCryptoData(null);
            dict.AddOrReplace(cryptoData);
            SetCryptoData(dict);
        }

        public void SetCryptoData(CryptoDataDictionary cryptoData)
        {
            var obj = new JObject();
            var serializer = new Serializer(Dummy);
#pragma warning disable CS0618
            foreach (var v in cryptoData)
            {
                var clone = serializer.ToObject<CryptoData>(serializer.ToString(v));
                clone.CryptoCode = null;
                clone.PaymentType = null;
                obj.Add(new JProperty(v.GetId().ToString(), JObject.Parse(serializer.ToString(clone))));
            }
            CryptoData = obj;
#pragma warning restore CS0618
        }
    }

    public class CryptoDataAccounting
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
        public int TxCount { get; set; }
        /// <summary>
        /// Total amount of network fee to pay to the invoice
        /// </summary>
        public Money NetworkFee { get; set; }
    }

    public interface IPaymentMethod
    {
        string GetPaymentDestination();
        PaymentTypes GetPaymentType();
        Money GetTxFee();
        void SetPaymentDestination(string newPaymentDestination);
    }

    public class CryptoDataId
    {
        public CryptoDataId(string cryptoCode, PaymentTypes paymentType)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            PaymentType = paymentType;
            CryptoCode = cryptoCode;
        }
        public string CryptoCode { get; private set; }
        public PaymentTypes PaymentType { get; private set; }


        public override bool Equals(object obj)
        {
            CryptoDataId item = obj as CryptoDataId;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString(), StringComparison.InvariantCulture);
        }
        public static bool operator ==(CryptoDataId a, CryptoDataId b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(CryptoDataId a, CryptoDataId b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
#pragma warning disable CA1307 // Specify StringComparison
            return ToString().GetHashCode();
#pragma warning restore CA1307 // Specify StringComparison
        }

        public override string ToString()
        {
            if (PaymentType == PaymentTypes.BTCLike)
                return CryptoCode;
            return CryptoCode + "_" + PaymentType.ToString();
        }

        public static CryptoDataId Parse(string str)
        {
            var parts = str.Split('_');
            return new CryptoDataId(parts[0], parts.Length == 1 ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(parts[1]));
        }
    }

    public class CryptoData
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


        public CryptoDataId GetId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new CryptoDataId(CryptoCode, string.IsNullOrEmpty(PaymentType) ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(PaymentType));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetId(CryptoDataId id)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            CryptoCode = id.CryptoCode;
            PaymentType = id.PaymentType.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonProperty(PropertyName = "rate")]
        public decimal Rate { get; set; }

        [Obsolete("Use GetPaymentMethod() instead")]
        public JObject PaymentMethod { get; set; }
        public IPaymentMethod GetPaymentMethod()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            // Legacy, old code does not have PaymentMethods
            if (string.IsNullOrEmpty(PaymentType) || PaymentMethod == null)
            {
                return new BitcoinLikeOnChainPaymentMethod()
                {
                    FeeRate = FeeRate,
                    DepositAddress = string.IsNullOrEmpty(DepositAddress) ? null : BitcoinAddress.Create(DepositAddress, Network?.NBitcoinNetwork),
                    TxFee = TxFee
                };
            }
            else
            {

                if (GetId().PaymentType == PaymentTypes.BTCLike)
                {
                    var method = DeserializePaymentMethod<BitcoinLikeOnChainPaymentMethod>(PaymentMethod);
                    method.TxFee = TxFee;
                    method.DepositAddress = BitcoinAddress.Create(DepositAddress, Network?.NBitcoinNetwork);
                    method.FeeRate = FeeRate;
                    return method;
                }
            }
            throw new NotSupportedException(PaymentType);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private T DeserializePaymentMethod<T>(JObject jobj) where T : class, IPaymentMethod
        {
            return JsonConvert.DeserializeObject<T>(jobj.ToString());
        }

        public void SetPaymentMethod(IPaymentMethod paymentMethod)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            // Legacy, need to fill the old fields

            if (PaymentType == null)
                PaymentType = paymentMethod.GetPaymentType().ToString();
            else if (PaymentType != paymentMethod.GetPaymentType().ToString())
                throw new InvalidOperationException("Invalid payment method affected");

            if (paymentMethod is BitcoinLikeOnChainPaymentMethod bitcoinPaymentMethod)
            {
                TxFee = bitcoinPaymentMethod.TxFee;
                FeeRate = bitcoinPaymentMethod.FeeRate;
                DepositAddress = bitcoinPaymentMethod.DepositAddress.ToString();
            }
            var jobj = JObject.Parse(JsonConvert.SerializeObject(paymentMethod));
            PaymentMethod = jobj;
           
#pragma warning restore CS0618 // Type or member is obsolete
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

        [JsonIgnore]
        public bool IsPhantomBTC { get; set; }

        public CryptoDataAccounting Calculate()
        {
            var cryptoData = ParentEntity.GetCryptoData(null, IsPhantomBTC);
            var totalDue = Money.Coins(ParentEntity.ProductInformation.Price / Rate);
            var paid = Money.Zero;
            var cryptoPaid = Money.Zero;

            var paidTxFee = Money.Zero;
            bool paidEnough = totalDue <= paid;
            int txCount = 0;
            var payments =
                ParentEntity.GetPayments()
                .Where(p => p.Accounted)
                .OrderBy(p => p.ReceivedTime)
                .Select(_ =>
                {
                    var txFee = _.GetValue(cryptoData, GetId(), cryptoData[_.GetCryptoDataId()].GetTxFee());
                    paid += _.GetValue(cryptoData, GetId());
                    if (!paidEnough)
                    {
                        totalDue += txFee;
                        paidTxFee += txFee;
                    }
                    paidEnough |= totalDue <= paid;
                    if (GetId() == _.GetCryptoDataId())
                    {
                        cryptoPaid += _.GetCryptoPaymentData().GetValue();
                        txCount++;
                    }
                    return _;
                })
                .ToArray();

            if (!paidEnough)
            {
                txCount++;
                totalDue += GetTxFee();
                paidTxFee += GetTxFee();
            }
            var accounting = new CryptoDataAccounting();
            accounting.TotalDue = totalDue;
            accounting.Paid = paid;
            accounting.TxCount = txCount;
            accounting.CryptoPaid = cryptoPaid;
            accounting.Due = Money.Max(accounting.TotalDue - accounting.Paid, Money.Zero);
            accounting.NetworkFee = paidTxFee;
            return accounting;
        }

        private Money GetTxFee()
        {
            var method = GetPaymentMethod();
            if (method == null)
                return Money.Zero;
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


        [Obsolete("Use GetCryptoDataId().CryptoCode instead")]
        public string CryptoCode
        {
            get;
            set;
        }

        [Obsolete("Use GetCryptoPaymentData() instead")]
        public string CryptoPaymentData { get; set; }
        [Obsolete("Use GetCryptoDataId().PaymentType instead")]
        public string CryptoPaymentDataType { get; set; }


        public CryptoPaymentData GetCryptoPaymentData()
        {
#pragma warning disable CS0618
            if (string.IsNullOrEmpty(CryptoPaymentDataType))
            {
                // In case this is a payment done before this update, consider it unconfirmed with RBF for safety
                var paymentData = new BitcoinLikePaymentData();
                paymentData.Outpoint = Outpoint;
                paymentData.Output = Output;
                paymentData.RBF = true;
                paymentData.ConfirmationCount = 0;
                paymentData.Legacy = true;
                return paymentData;
            }
            if (GetCryptoDataId().PaymentType == PaymentTypes.BTCLike)
            {
                var paymentData = JsonConvert.DeserializeObject<BitcoinLikePaymentData>(CryptoPaymentData);
                // legacy
                paymentData.Output = Output;
                paymentData.Outpoint = Outpoint;
                return paymentData;
            }

            throw new NotSupportedException(nameof(CryptoPaymentDataType) + " does not support " + CryptoPaymentDataType);
#pragma warning restore CS0618
        }

        public void SetCryptoPaymentData(CryptoPaymentData cryptoPaymentData)
        {
#pragma warning disable CS0618
            if (cryptoPaymentData is BitcoinLikePaymentData paymentData)
            {
                // Legacy
                Outpoint = paymentData.Outpoint;
                Output = paymentData.Output;
                ///
            }
            else
                throw new NotSupportedException(cryptoPaymentData.ToString());
            CryptoPaymentDataType = paymentData.GetPaymentType().ToString();
            CryptoPaymentData = JsonConvert.SerializeObject(cryptoPaymentData);
#pragma warning restore CS0618
        }
        public Money GetValue(CryptoDataDictionary cryptoData, CryptoDataId cryptoDataId, Money value = null)
        {
#pragma warning disable CS0618
            value = value ?? Output.Value;
#pragma warning restore CS0618
            var to = cryptoDataId;
            var from = this.GetCryptoDataId();
            if (to == from)
                return value;
            var fromRate = cryptoData[from].Rate;
            var toRate = cryptoData[to].Rate;

            var fiatValue = fromRate * value.ToDecimal(MoneyUnit.BTC);
            var otherCurrencyValue = toRate == 0 ? 0.0m : fiatValue / toRate;
            return Money.Coins(otherCurrencyValue);
        }

        public CryptoDataId GetCryptoDataId()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new CryptoDataId(CryptoCode ?? "BTC", string.IsNullOrEmpty(CryptoPaymentDataType) ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(CryptoPaymentDataType));
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
        Money GetValue();
        bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network);
        bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network);

    }
}
