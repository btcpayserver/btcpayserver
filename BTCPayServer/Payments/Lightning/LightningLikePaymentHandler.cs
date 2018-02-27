using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentHandler : PaymentMethodHandlerBase<LightningSupportedPaymentMethod>
    {
        ExplorerClientProvider _ExplorerClientProvider;
        public LightningLikePaymentHandler(ExplorerClientProvider explorerClientProvider)
        {
            _ExplorerClientProvider = explorerClientProvider;
        }
        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(LightningSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network)
        {
            var invoice = paymentMethod.ParentEntity;
            var due = invoice.ProductInformation.Price / paymentMethod.Rate;
            var client = GetClient(supportedPaymentMethod, network);
            var expiry = invoice.ExpirationTime - DateTimeOffset.UtcNow;
            var lightningInvoice = await client.CreateInvoiceAsync(new CreateInvoiceRequest()
            {
                Amont = new LightMoney(due, LightMoneyUnit.BTC),
                Expiry = expiry < TimeSpan.Zero ? TimeSpan.FromSeconds(1) : expiry
            });
            return new LightningLikePaymentMethodDetails()
            {
                BOLT11 = lightningInvoice.PayReq,
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

        public async Task Test(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            if (!_ExplorerClientProvider.IsAvailable(network))
                throw new Exception($"Full node not available");

            var explorerClient = _ExplorerClientProvider.GetExplorerClient(network);
            var cts = new CancellationTokenSource(5000);
            var client = GetClient(supportedPaymentMethod, network);
            var status = explorerClient.GetStatusAsync();
            GetInfoResponse info = null;
            try
            {

                info = await client.GetInfoAsync(cts.Token);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while connecting to the lightning charge {client.Uri} ({ex.Message})");
            }
            var address = info.Address?.FirstOrDefault();
            var port = info.Port;
            address = address ?? client.Uri.DnsSafeHost;

            if (info.Network != network.CLightningNetworkName)
            {
                throw new Exception($"Lightning node network {info.Network}, but expected is {network.CLightningNetworkName}");
            }

            if (Math.Abs(info.BlockHeight - (await status).ChainHeight) > 10)
            {
                throw new Exception($"The lightning node is not synched");
            }

            try
            {
                await TestConnection(address, port, cts.Token);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while connecting to the lightning node via {address} ({ex.Message})");
            }
        }

        private static ChargeClient GetClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            return new ChargeClient(supportedPaymentMethod.GetLightningChargeUrl(true), network.NBitcoinNetwork);
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
