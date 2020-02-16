using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using NBitcoin;
using System.Globalization;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentHandler : PaymentMethodHandlerBase<LightningSupportedPaymentMethod, BTCPayNetwork>
    {
        public static int LIGHTNING_TIMEOUT = 5000;

        NBXplorerDashboard _Dashboard;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly SocketFactory _socketFactory;

        public LightningLikePaymentHandler(
            NBXplorerDashboard dashboard,
            LightningClientFactoryService lightningClientFactory,
            BTCPayNetworkProvider networkProvider,
            SocketFactory socketFactory)
        {
            _Dashboard = dashboard;
            _lightningClientFactory = lightningClientFactory;
            _networkProvider = networkProvider;
            _socketFactory = socketFactory;
        }

        public override PaymentType PaymentType => PaymentTypes.LightningLike;
        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            LightningSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store,
            BTCPayNetwork network, object preparePaymentObject)
        {
            //direct casting to (BTCPayNetwork) is fixed in other pull requests with better generic interfacing for handlers
            var storeBlob = store.GetStoreBlob();
            var test = GetNodeInfo(paymentMethod.PreferOnion, supportedPaymentMethod, (BTCPayNetwork)network);
            var invoice = paymentMethod.ParentEntity;
            var due = Extensions.RoundUp(invoice.ProductInformation.Price / paymentMethod.Rate, network.Divisibility);
            var client = _lightningClientFactory.Create(supportedPaymentMethod.GetLightningUrl(), (BTCPayNetwork)network);
            var expiry = invoice.ExpirationTime - DateTimeOffset.UtcNow;
            if (expiry < TimeSpan.Zero)
                expiry = TimeSpan.FromSeconds(1);

            LightningInvoice lightningInvoice = null;

            string description = storeBlob.LightningDescriptionTemplate;
            description = description.Replace("{StoreName}", store.StoreName ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{ItemDescription}", invoice.ProductInformation.ItemDesc ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{OrderId}", invoice.OrderId ?? "", StringComparison.OrdinalIgnoreCase);
            using (var cts = new CancellationTokenSource(LIGHTNING_TIMEOUT))
            {
                try
                {
                    lightningInvoice = await client.CreateInvoice(new LightMoney(due, LightMoneyUnit.BTC), description, expiry, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException($"The lightning node did not reply in a timely manner");
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Impossible to create lightning invoice ({ex.Message})", ex);
                }
            }
            var nodeInfo = await test;
            return new LightningLikePaymentMethodDetails()
            {
                BOLT11 = lightningInvoice.BOLT11,
                InvoiceId = lightningInvoice.Id,
                NodeInfo = nodeInfo.ToString()
            };
        }

        public async Task<NodeInfo> GetNodeInfo(bool preferOnion, LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            if (!_Dashboard.IsFullySynched(network.CryptoCode, out var summary))
                throw new PaymentMethodUnavailableException($"Full node not available");

            using (var cts = new CancellationTokenSource(LIGHTNING_TIMEOUT))
            {
                var client = _lightningClientFactory.Create(supportedPaymentMethod.GetLightningUrl(), network);
                LightningNodeInformation info = null;
                try
                {
                    info = await client.GetInfo(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException($"The lightning node did not reply in a timely manner");
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Error while connecting to the API ({ex.Message})");
                }
                var nodeInfo = info.NodeInfoList.FirstOrDefault(i => i.IsTor == preferOnion) ?? info.NodeInfoList.FirstOrDefault();
                if (nodeInfo == null)
                {
                    throw new PaymentMethodUnavailableException($"No lightning node public address has been configured");
                }

                var blocksGap = summary.Status.ChainHeight - info.BlockHeight;
                if (blocksGap > 10)
                {
                    throw new PaymentMethodUnavailableException($"The lightning node is not synched ({blocksGap} blocks left)");
                }

                return nodeInfo;
            }
        }

        public async Task TestConnection(NodeInfo nodeInfo, CancellationToken cancellation)
        {
            try
            {
                if (!Utils.TryParseEndpoint(nodeInfo.Host, nodeInfo.Port, out var endpoint))
                    throw new PaymentMethodUnavailableException($"Could not parse the endpoint {nodeInfo.Host}");

                using (var tcp = await _socketFactory.ConnectAsync(endpoint, cancellation))
                {
                }
            }
            catch (Exception ex)
            {
                throw new PaymentMethodUnavailableException($"Error while connecting to the lightning node via {nodeInfo.Host}:{nodeInfo.Port} ({ex.Message})");
            }
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider
                .GetAll()
                .OfType<BTCPayNetwork>()
                .Where(network => network.NBitcoinNetwork.Consensus.SupportSegwit)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike));
        }
        
        
        public override async Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount, PaymentMethodId paymentMethodId)
        {
            if (storeBlob.LightningMaxValue != null)
            {
                var currentRateToCrypto = await rate[new CurrencyPair(paymentMethodId.CryptoCode, storeBlob.LightningMaxValue.Currency)];

                if (currentRateToCrypto?.BidAsk != null)
                {
                    var limitValueCrypto = Money.Coins(storeBlob.LightningMaxValue.Value / currentRateToCrypto.BidAsk.Bid);
                    if (amount > limitValueCrypto)
                    {
                        return "The amount of the invoice is too high to be paid with lightning";
                    }
                }
            }
            return string.Empty;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentTypes.LightningLike);
            
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);
            model.IsLightning = true;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.InvoiceBitcoinUrl = cryptoInfo.PaymentUrls.BOLT11;
            model.InvoiceBitcoinUrlQR = $"lightning:{cryptoInfo.PaymentUrls.BOLT11.ToUpperInvariant().Substring("LIGHTNING:".Length)}";
            model.LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi;
            if (storeBlob.LightningAmountInSatoshi && model.CryptoCode == "BTC" )
            {
                var satoshiCulture = new CultureInfo(CultureInfo.InvariantCulture.Name);
                satoshiCulture.NumberFormat.NumberGroupSeparator = " ";

                model.CryptoCode = "Sats";
                model.BtcDue = Money.Parse(model.BtcDue).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
                model.BtcPaid =  Money.Parse(model.BtcPaid).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
                model.OrderAmount = Money.Parse(model.OrderAmount).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);

                model.NetworkFee = new Money(model.NetworkFee, MoneyUnit.BTC).ToUnit(MoneyUnit.Satoshi);
            }
        }
        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }
        
        private string GetCryptoImage(BTCPayNetworkBase network)
        {
            return ((BTCPayNetwork)network).LightningImagePath;
        }
        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }
        
        private string GetPaymentMethodName(BTCPayNetworkBase network)
        {
            return $"{network.DisplayName} (Lightning)";
        }
    }
}
