using BTCPayServer.Filters;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        [HttpGet]
        [Route("i/{invoiceId}")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest")]
        public async Task<IActionResult> GetInvoiceRequest(string invoiceId, string cryptoCode = null)
        {
            if (cryptoCode == null)
                cryptoCode = "BTC";
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            var network = _NetworkProvider.GetNetwork(cryptoCode);
            if (invoice == null || invoice.IsExpired() || network == null || !invoice.Support(network))
                return NotFound();

            var dto = invoice.EntityToDTO(_NetworkProvider);
            var cryptoData = dto.CryptoInfo.First(c => c.CryptoCode.Equals(cryptoCode, StringComparison.OrdinalIgnoreCase));
            PaymentRequest request = new PaymentRequest
            {
                DetailsVersion = 1
            };
            request.Details.Expires = invoice.ExpirationTime;
            request.Details.Memo = invoice.ProductInformation.ItemDesc;
            request.Details.Network = network.NBitcoinNetwork;
            request.Details.Outputs.Add(new PaymentOutput() { Amount = cryptoData.Due, Script = BitcoinAddress.Create(cryptoData.Address, network.NBitcoinNetwork).ScriptPubKey });
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
            if (network == null || invoice == null || invoice.IsExpired() || !invoice.Support(network))
                return NotFound();

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
                return NotFound();
            var payment = PaymentMessage.Load(Request.Body);
            var unused = wallet.BroadcastTransactionsAsync(payment.Transactions);
            await _InvoiceRepository.AddRefundsAsync(invoiceId, payment.RefundTo.Select(p => new TxOut(p.Amount, p.Script)).ToArray());
            return new PaymentAckActionResult(payment.CreateACK(invoiceId + " is currently processing, thanks for your purchase..."));
        }
    }
}
