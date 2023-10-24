#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.LndHub;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentHandler : PaymentMethodHandlerBase<LightningSupportedPaymentMethod, BTCPayNetwork>
    {
        public static readonly int LightningTimeout = 5000;
        readonly NBXplorerDashboard _Dashboard;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly SocketFactory _socketFactory;
        private readonly DisplayFormatter _displayFormatter;

        public LightningLikePaymentHandler(
            NBXplorerDashboard dashboard,
            LightningClientFactoryService lightningClientFactory,
            BTCPayNetworkProvider networkProvider,
            SocketFactory socketFactory,
            DisplayFormatter displayFormatter,
            IOptions<LightningNetworkOptions> options)
        {
            _Dashboard = dashboard;
            _lightningClientFactory = lightningClientFactory;
            _networkProvider = networkProvider;
            _socketFactory = socketFactory;
            _displayFormatter = displayFormatter;
            Options = options;
        }

        public override PaymentType PaymentType => PaymentTypes.LightningLike;

        public IOptions<LightningNetworkOptions> Options { get; }

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            InvoiceLogs logs,
            LightningSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, Data.StoreData store,
            BTCPayNetwork network, object preparePaymentObject, IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            if (paymentMethod.ParentEntity.Type == InvoiceType.TopUp)
            {
                throw new PaymentMethodUnavailableException("Lightning Network payment method is not available for top-up invoices");
            }

            if (preparePaymentObject is null)
            {
                return new LightningLikePaymentMethodDetails()
                {
                    Activated = false
                };
            }
            var storeBlob = store.GetStoreBlob();
            var nodeInfo = GetNodeInfo(supportedPaymentMethod, network, logs, paymentMethod.PreferOnion);

            var invoice = paymentMethod.ParentEntity;
            decimal due = Extensions.RoundUp(invoice.Price / paymentMethod.Rate, network.Divisibility);
            try
            {
                due = paymentMethod.Calculate().Due;
            }
            catch (Exception)
            {
                // ignored
            }
            var client = supportedPaymentMethod.CreateLightningClient(network, Options.Value, _lightningClientFactory);
            var expiry = invoice.ExpirationTime - DateTimeOffset.UtcNow;
            if (expiry < TimeSpan.Zero)
                expiry = TimeSpan.FromSeconds(1);

            LightningInvoice? lightningInvoice;

            string description = storeBlob.LightningDescriptionTemplate;
            description = description.Replace("{StoreName}", store.StoreName ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{ItemDescription}", invoice.Metadata.ItemDesc ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{OrderId}", invoice.Metadata.OrderId ?? "", StringComparison.OrdinalIgnoreCase);
            using (var cts = new CancellationTokenSource(LightningTimeout))
            {
                try
                {
                    var request = new CreateInvoiceParams(new LightMoney(due, LightMoneyUnit.BTC), description, expiry);
                    request.PrivateRouteHints = storeBlob.LightningPrivateRouteHints;
                    lightningInvoice = await client.CreateInvoice(request, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException("The lightning node did not reply in a timely manner");
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Impossible to create lightning invoice ({ex.Message})", ex);
                }
            }

            return new LightningLikePaymentMethodDetails
            {
                Activated = true,
                BOLT11 = lightningInvoice.BOLT11,
                PaymentHash = BOLT11PaymentRequest.Parse(lightningInvoice.BOLT11, network.NBitcoinNetwork).PaymentHash,
                Preimage = string.IsNullOrEmpty(lightningInvoice.Preimage) ? null : uint256.Parse(lightningInvoice.Preimage),
                InvoiceId = lightningInvoice.Id,
                NodeInfo = (await nodeInfo).FirstOrDefault()?.ToString()
            };
        }

        public async Task<NodeInfo[]> GetNodeInfo(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network, InvoiceLogs invoiceLogs, bool? preferOnion = null, bool throws = false)
        {
            var synced = _Dashboard.IsFullySynched(network.CryptoCode, out var summary);
            if (supportedPaymentMethod.IsInternalNode && !synced)
                throw new PaymentMethodUnavailableException("Full node not available");;
            try
            {
                using var cts = new CancellationTokenSource(LightningTimeout);
                var client = CreateLightningClient(supportedPaymentMethod, network);

                // LNDhub-compatible implementations might not offer all of GetInfo data.
                // Skip checks in those cases, see https://github.com/lnbits/lnbits/issues/1182
                var isLndHub = client is LndHubLightningClient;
                
                LightningNodeInformation info;
                try
                {
                    info = await client.GetInfo(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException("The lightning node did not reply in a timely manner");
                }
                catch (NotSupportedException) when (isLndHub)
                {
                    return new NodeInfo[] {};
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Error while connecting to the API: {ex.Message}" +
                                                                (!string.IsNullOrEmpty(ex.InnerException?.Message) ? $" ({ex.InnerException.Message})" : ""));
                }

                // Node info might be empty if there are no public URIs to announce. The UI also supports this.
                var nodeInfo = preferOnion != null && info.NodeInfoList.Any(i => i.IsTor == preferOnion)
                    ? info.NodeInfoList.Where(i => i.IsTor == preferOnion.Value).ToArray()
                    : info.NodeInfoList.Select(i => i).ToArray();

                var blocksGap = summary.Status.ChainHeight - info.BlockHeight;
                if (blocksGap > 10 && !(isLndHub && info.BlockHeight == 0))
                {
                    throw new PaymentMethodUnavailableException(
                        $"The lightning node is not synched ({blocksGap} blocks left)");
                }
                return nodeInfo;
            }
            catch (Exception e) when (!throws)
            {
                invoiceLogs.Write($"NodeInfo failed to be fetched: {e.Message}", InvoiceEventData.EventSeverity.Error);
            }

            return Array.Empty<NodeInfo>();
        }

        public ILightningClient CreateLightningClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            return supportedPaymentMethod.CreateLightningClient(network, Options.Value, _lightningClientFactory);
        }

        public async Task TestConnection(NodeInfo nodeInfo, CancellationToken cancellation)
        {
            try
            {
                if (!Utils.TryParseEndpoint(nodeInfo.Host, nodeInfo.Port, out var endpoint))
                    throw new PaymentMethodUnavailableException($"Could not parse the endpoint {nodeInfo.Host}");

                using var tcp = await _socketFactory.ConnectAsync(endpoint, cancellation);
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
                .Where(network => network.NBitcoinNetwork.Consensus.SupportSegwit && network.SupportLightning)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike));
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);

            model.PaymentMethodName = GetPaymentMethodName(network);
            model.InvoiceBitcoinUrl = cryptoInfo.PaymentUrls?.BOLT11;
            model.InvoiceBitcoinUrlQR = $"lightning:{cryptoInfo.PaymentUrls?.BOLT11?.ToUpperInvariant()?.Substring("LIGHTNING:".Length)}";
            model.PeerInfo = ((LightningLikePaymentMethodDetails)paymentMethod.GetPaymentMethodDetails()).NodeInfo;

            if (storeBlob.LightningAmountInSatoshi && model.CryptoCode == "BTC")
            {
                base.PreparePaymentModelForAmountInSats(model, paymentMethod, _displayFormatter);
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

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Lightning/LightningLikeMethodCheckout",
                CheckoutBodyVueComponentName = "LightningLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "LightningLikeMethodCheckoutHeader",
                NoScriptPartialName = "Lightning/LightningLikeMethodCheckoutNoScript"
            };
        }

        private string GetPaymentMethodName(BTCPayNetworkBase network)
        {
            return $"{network.DisplayName} (Lightning)";
        }

        public override object PreparePayment(LightningSupportedPaymentMethod supportedPaymentMethod, Data.StoreData store,
            BTCPayNetworkBase network)
        {
            // pass a non null obj, so that if lazy payment feature is used, it has a marker to trigger activation
            return new { };
        }
    }
}
