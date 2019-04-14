using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentHandler : PaymentMethodHandlerBase<LightningSupportedPaymentMethod>
    {
        public static int LIGHTNING_TIMEOUT = 5000;

        NBXplorerDashboard _Dashboard;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly SocketFactory _socketFactory;

        public LightningLikePaymentHandler(
            NBXplorerDashboard dashboard,
            LightningClientFactoryService lightningClientFactory,
            SocketFactory socketFactory)
        {
            _Dashboard = dashboard;
            _lightningClientFactory = lightningClientFactory;
            _socketFactory = socketFactory;
        }
        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(LightningSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject)
        {
            var storeBlob = store.GetStoreBlob();
            var test = GetNodeInfo(paymentMethod.PreferOnion, supportedPaymentMethod, network);
            var invoice = paymentMethod.ParentEntity;
            var due = Extensions.RoundUp(invoice.ProductInformation.Price / paymentMethod.Rate, 8);
            var client = _lightningClientFactory.Create(supportedPaymentMethod.GetLightningUrl(), network);
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
                    throw new PaymentMethodUnavailableException($"The lightning node did not reply in a timely maner");
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
    }
}
