using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Lightning.Charge;
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentHandler : PaymentMethodHandlerBase<LightningSupportedPaymentMethod>
    {
        NBXplorerDashboard _Dashboard;
        LightningClientFactory _LightningClientFactory;
        public LightningLikePaymentHandler(
            LightningClientFactory lightningClientFactory,
            NBXplorerDashboard dashboard)
        {
            _LightningClientFactory = lightningClientFactory;
            _Dashboard = dashboard;
        }
        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(LightningSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network)
        {
            var invoice = paymentMethod.ParentEntity;
            var due = Extensions.RoundUp(invoice.ProductInformation.Price / paymentMethod.Rate, 8);
            var client = _LightningClientFactory.CreateClient(supportedPaymentMethod, network);
            var expiry = invoice.ExpirationTime - DateTimeOffset.UtcNow;
            if (expiry < TimeSpan.Zero)
                expiry = TimeSpan.FromSeconds(1);
            var lightningInvoice = await client.CreateInvoice(new LightMoney(due, LightMoneyUnit.BTC), expiry);
            return new LightningLikePaymentMethodDetails()
            {
                BOLT11 = lightningInvoice.BOLT11,
                InvoiceId = lightningInvoice.Id
            };
        }

        public async override Task<bool> IsAvailable(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            try
            {
                await Test(supportedPaymentMethod, network);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Used for testing
        /// </summary>
        public bool SkipP2PTest { get; set; }

        public async Task Test(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            if (!_Dashboard.IsFullySynched(network.CryptoCode, out var summary))
                throw new Exception($"Full node not available");
            
            var cts = new CancellationTokenSource(5000);
            var client = _LightningClientFactory.CreateClient(supportedPaymentMethod, network);
            LightningNodeInformation info = null;
            try
            {
                info = await client.GetInfo(cts.Token);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while connecting to the API ({ex.Message})");
            }

            if(info.Address == null)
            {
                throw new Exception($"No lightning node public address has been configured");
            }

            var blocksGap = Math.Abs(info.BlockHeight - summary.Status.ChainHeight);
            if (blocksGap > 10)
            {
                throw new Exception($"The lightning is not synched ({blocksGap} blocks)");
            }

            try
            {
                if(!SkipP2PTest)
                    await TestConnection(info.Address, info.P2PPort, cts.Token);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while connecting to the lightning node via {info.Address}:{info.P2PPort} ({ex.Message})");
            }
        }

        private async Task<bool> TestConnection(string addressStr, int port, CancellationToken cancellation)
        {
            IPAddress address = null;
            try
            {
                address = IPAddress.Parse(addressStr);
            }
            catch
            {
                try
                {
                    address = (await Dns.GetHostAddressesAsync(addressStr)).FirstOrDefault();
                }
                catch { }
            }

            if (address == null)
                throw new Exception($"DNS did not resolved {addressStr}");

            using (var tcp = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    await WithTimeout(tcp.ConnectAsync(new IPEndPoint(address, port)), cancellation);
                }
                catch { return false; }
            }
            return true;
        }

        static Task WithTimeout(Task task, CancellationToken token)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            var registration = token.Register(() => { try { tcs.TrySetResult(true); } catch { } });
#pragma warning disable CA2008 // Do not create tasks without passing a TaskScheduler
            var timeoutTask = tcs.Task;
#pragma warning restore CA2008 // Do not create tasks without passing a TaskScheduler
            return Task.WhenAny(task, timeoutTask).Unwrap().ContinueWith(t => registration.Dispose(), TaskScheduler.Default);
        }
    }
}
