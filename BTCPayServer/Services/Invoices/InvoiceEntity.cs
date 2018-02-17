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
            JObject strategies = JObject.Parse(DerivationStrategies);
#pragma warning restore CS0618
            foreach (var strat in strategies.Properties())
            {
                if (strat.Name == network.CryptoCode)
                {
                    return BTCPayServer.DerivationStrategy.Parse(strat.Value.Value<string>(), network).DerivationStrategyBase;
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
            foreach (var info in this.GetCryptoData(networkProvider, true).Values)
            {
                var accounting = info.Calculate();
                var cryptoInfo = new NBitpayClient.InvoiceCryptoInfo();
                cryptoInfo.CryptoCode = info.CryptoCode;
                cryptoInfo.Rate = info.Rate;
                cryptoInfo.Price = Money.Coins(ProductInformation.Price / cryptoInfo.Rate).ToString();

                cryptoInfo.Due = accounting.Due.ToString();
                cryptoInfo.Paid = accounting.Paid.ToString();
                cryptoInfo.TotalDue = accounting.TotalDue.ToString();
                cryptoInfo.NetworkFee = accounting.NetworkFee.ToString();
                cryptoInfo.TxCount = accounting.TxCount;
                cryptoInfo.CryptoPaid = accounting.CryptoPaid.ToString();

                cryptoInfo.Address = info.DepositAddress;
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

        internal bool Support(BTCPayNetwork network)
        {
            var rates = GetCryptoData(null);
            return rates.TryGetValue(network.CryptoCode, out var data);
        }

        public CryptoData GetCryptoData(string cryptoCode, BTCPayNetworkProvider networkProvider, bool alwaysIncludeBTC = false)
        {
            GetCryptoData(networkProvider, alwaysIncludeBTC).TryGetValue(cryptoCode, out var data);
            return data;
        }

        public CryptoData GetCryptoData(BTCPayNetwork network, BTCPayNetworkProvider networkProvider)
        {
            GetCryptoData(networkProvider).TryGetValue(network.CryptoCode, out var data);
            return data;
        }

        public Dictionary<string, CryptoData> GetCryptoData(BTCPayNetworkProvider networkProvider, bool alwaysIncludeBTC = false)
        {
            Dictionary<string, CryptoData> rates = new Dictionary<string, CryptoData>();
            var serializer = new Serializer(Dummy);
            CryptoData phantom = null;
#pragma warning disable CS0618
            // Legacy
            if (alwaysIncludeBTC)
            {
                var btcNetwork = networkProvider?.GetNetwork("BTC");
                phantom = new CryptoData() { ParentEntity = this, IsPhantomBTC = true, Rate = Rate, CryptoCode = "BTC", TxFee = TxFee, FeeRate = new FeeRate(TxFee, 100), DepositAddress = DepositAddress, Network = btcNetwork };
                rates.Add("BTC", phantom);
            }
            if (CryptoData != null)
            {
                foreach (var prop in CryptoData.Properties())
                {
                    if (prop.Name == "BTC" && phantom != null)
                        rates.Remove("BTC");
                    var r = serializer.ToObject<CryptoData>(prop.Value.ToString());
                    r.CryptoCode = prop.Name;
                    r.ParentEntity = this;
                    r.Network = networkProvider?.GetNetwork(r.CryptoCode);
                    rates.Add(r.CryptoCode, r);
                }
            }
#pragma warning restore CS0618
            return rates;
        }

        Network Dummy = Network.Main;

        public void SetCryptoData(CryptoData cryptoData)
        {
            var dict = GetCryptoData(null);
            dict.AddOrReplace(cryptoData.CryptoCode, cryptoData);
            SetCryptoData(dict);
        }

        public void SetCryptoData(Dictionary<string, CryptoData> cryptoData)
        {
            var obj = new JObject();
            var serializer = new Serializer(Dummy);
            foreach (var kv in cryptoData)
            {
                var clone = serializer.ToObject<CryptoData>(serializer.ToString(kv.Value));
                clone.CryptoCode = null;
                obj.Add(new JProperty(kv.Key, JObject.Parse(serializer.ToString(clone))));
            }
#pragma warning disable CS0618
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

    public class CryptoData
    {
        [JsonIgnore]
        public InvoiceEntity ParentEntity { get; set; }
        [JsonIgnore]
        public BTCPayNetwork Network { get; set; }
        [JsonProperty(PropertyName = "cryptoCode", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string CryptoCode { get; set; }
        [JsonProperty(PropertyName = "rate")]
        public decimal Rate { get; set; }
        [JsonProperty(PropertyName = "feeRate")]
        public FeeRate FeeRate { get; set; }
        [JsonProperty(PropertyName = "txFee")]
        public Money TxFee { get; set; }
        [JsonProperty(PropertyName = "depositAddress")]
        public string DepositAddress { get; set; }

        public BitcoinAddress GetDepositAddress()
        {
            if (string.IsNullOrEmpty(DepositAddress))
            {
                return null;
            }
            return BitcoinAddress.Create(DepositAddress, Network.NBitcoinNetwork);
        }

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
                    var txFee = _.GetValue(cryptoData, CryptoCode, cryptoData[_.GetCryptoCode()].TxFee);
                    paid += _.GetValue(cryptoData, CryptoCode);
                    if (!paidEnough)
                    {
                        totalDue += txFee;
                        paidTxFee += txFee;
                    }
                    paidEnough |= totalDue <= paid;
                    if (CryptoCode == _.GetCryptoCode())
                    {
                        cryptoPaid += _.GetValue();
                        txCount++;
                    }
                    return _;
                })
                .ToArray();

            if (!paidEnough)
            {
                txCount++;
                totalDue += TxFee;
                paidTxFee += TxFee;
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

        public Script GetScriptPubKey()
        {
#pragma warning disable CS0618
            return Output.ScriptPubKey;
#pragma warning restore CS0618
        }

        public bool Accounted
        {
            get; set;
        }


        [Obsolete("Use GetCryptoCode() instead")]
        public string CryptoCode
        {
            get;
            set;
        }

        [Obsolete("Use GetCryptoPaymentData() instead")]
        public string CryptoPaymentData { get; set; }
        [Obsolete("Use GetCryptoPaymentData() instead")]
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
            if (CryptoPaymentDataType == "BTCLike")
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
                CryptoPaymentDataType = "BTCLike";
                // Legacy
                Outpoint = paymentData.Outpoint;
                Output = paymentData.Output;
                ///
            }
            else
                throw new NotSupportedException(cryptoPaymentData.ToString());
            CryptoPaymentData = JsonConvert.SerializeObject(cryptoPaymentData);
#pragma warning restore CS0618
        }

        public Money GetValue()
        {
#pragma warning disable CS0618
            return Output.Value;
#pragma warning restore CS0618
        }
        public Money GetValue(Dictionary<string, CryptoData> cryptoData, string cryptoCode, Money value = null)
        {
#pragma warning disable CS0618
            value = value ?? Output.Value;
#pragma warning restore CS0618
            var to = cryptoCode;
            var from = GetCryptoCode();
            if (to == from)
                return value;
            var fromRate = cryptoData[from].Rate;
            var toRate = cryptoData[to].Rate;

            var fiatValue = fromRate * value.ToDecimal(MoneyUnit.BTC);
            var otherCurrencyValue = toRate == 0 ? 0.0m : fiatValue / toRate;
            return Money.Coins(otherCurrencyValue);
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
        bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network);
        bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network);

    }

    public class BitcoinLikePaymentData : CryptoPaymentData
    {
        public BitcoinLikePaymentData()
        {

        }
        public BitcoinLikePaymentData(Coin coin, bool rbf)
        {
            Outpoint = coin.Outpoint;
            Output = coin.TxOut;
            ConfirmationCount = 0;
            RBF = rbf;
        }
        [JsonIgnore]
        public OutPoint Outpoint { get; set; }
        [JsonIgnore]
        public TxOut Output { get; set; }
        public int ConfirmationCount { get; set; }
        public bool RBF { get; set; }

        /// <summary>
        /// This is set to true if the payment was created before CryptoPaymentData existed in BTCPayServer
        /// </summary>
        public bool Legacy { get; set; }

        public string GetPaymentId()
        {
            return Outpoint.ToString();
        }

        public string[] GetSearchTerms()
        {
            return new[] { Outpoint.Hash.ToString() };
        }

        public bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network)
        {
            return ConfirmationCount >= network.MaxTrackedConfirmation;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network)
        {
            if (speedPolicy == SpeedPolicy.HighSpeed)
            {
                return ConfirmationCount >= 1 || !RBF;
            }
            else if (speedPolicy == SpeedPolicy.MediumSpeed)
            {
                return ConfirmationCount >= 1;
            }
            else if (speedPolicy == SpeedPolicy.LowSpeed)
            {
                return ConfirmationCount >= 6;
            }
            return false;
        }
    }
}
