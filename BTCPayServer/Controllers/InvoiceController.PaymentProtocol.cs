using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Payment;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        [HttpGet]
        [Route("i/{invoiceId}/{cryptoCode?}")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest")]
        public async Task<IActionResult> GetInvoiceRequest(string invoiceId, string cryptoCode = null)
        {
            if (cryptoCode == null)
                cryptoCode = "BTC";
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            var network = _NetworkProvider.GetNetwork(cryptoCode);
            var paymentMethodId = new PaymentMethodId(cryptoCode, Payments.PaymentTypes.BTCLike);
            if (invoice == null || invoice.IsExpired() || network == null || !invoice.Support(paymentMethodId))
                return NotFound();

            var dto = invoice.EntityToDTO(_NetworkProvider);
            var paymentMethod = dto.CryptoInfo.First(c => c.GetpaymentMethodId() == paymentMethodId);
            PaymentRequest request = new PaymentRequest
            {
                DetailsVersion = 1
            };
            request.Details.Expires = invoice.ExpirationTime;
            request.Details.Memo = invoice.ProductInformation.ItemDesc;
            request.Details.Network = network.NBitcoinNetwork;
            request.Details.Outputs.Add(new PaymentOutput() { Amount = paymentMethod.Due, Script = BitcoinAddress.Create(paymentMethod.Address, network.NBitcoinNetwork).ScriptPubKey });
            request.Details.MerchantData = Encoding.UTF8.GetBytes(invoice.Id);
            request.Details.Time = DateTimeOffset.UtcNow;
            request.Details.PaymentUrl = new Uri(invoice.ServerUrl.WithTrailingSlash() + ($"i/{invoice.Id}"), UriKind.Absolute);

            var store = await _StoreRepository.FindStore(invoice.StoreId);
            if (store == null)
                throw new BitpayHttpException(401, "Unknown store");

            if (store.StoreCertificate != null)
            {
                try
                {
                    request.Sign(store.StoreCertificate, PKIType.X509SHA256);
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogWarning(ex, "Error while signing payment request");
                }
            }

            return new PaymentRequestActionResult(request);
        }

        [HttpPost]
        [Route("i/{invoiceId}", Order = 99)]
        [Route("i/{invoiceId}/{cryptoCode}", Order = 99)]
        [MediaTypeConstraint("application/bitcoin-payment")]
        public async Task<IActionResult> PostPayment(string invoiceId, string cryptoCode = null)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            if (cryptoCode == null)
                cryptoCode = "BTC";
            var network = _NetworkProvider.GetNetwork(cryptoCode);
            if (network == null || invoice == null || invoice.IsExpired() || !invoice.Support(new PaymentMethodId(cryptoCode, Payments.PaymentTypes.BTCLike)))
                return NotFound();

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
                return NotFound();
            var payment = PaymentMessage.Load(Request.Body, network.NBitcoinNetwork);
            var unused = wallet.BroadcastTransactionsAsync(payment.Transactions);
            await _InvoiceRepository.AddRefundsAsync(invoiceId, payment.RefundTo.Select(p => new TxOut(p.Amount, p.Script)).ToArray(), network.NBitcoinNetwork);
            return new PaymentAckActionResult(payment.CreateACK(invoiceId + " is currently processing, thanks for your purchase..."));
        }
    }


    public class PaymentRequestActionResult : IActionResult
    {
        PaymentRequest req;
        public PaymentRequestActionResult(PaymentRequest req)
        {
            this.req = req;
        }
        public Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers["Content-Transfer-Encoding"] = "binary";
            context.HttpContext.Response.ContentType = "application/bitcoin-paymentrequest";
            req.WriteTo(context.HttpContext.Response.Body);
            return Task.CompletedTask;
        }
    }
    public class PaymentAckActionResult : IActionResult
    {
        PaymentACK req;
        public PaymentAckActionResult(PaymentACK req)
        {
            this.req = req;
        }
        public Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers["Content-Transfer-Encoding"] = "binary";
            context.HttpContext.Response.ContentType = "application/bitcoin-paymentack";
            req.WriteTo(context.HttpContext.Response.Body);
            return Task.CompletedTask;
        }
    }
}
