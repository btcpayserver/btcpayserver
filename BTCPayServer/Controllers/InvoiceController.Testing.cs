using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using BTCPayServer.Services;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        public class FakePaymentRequest
        {
            public Decimal Amount { get; set; }
            public string CryptoCode { get; set; } = "BTC";
        }
        
        [HttpPost]
        [Route("i/{invoiceId}/test-payment")]
        [CheatModeRoute]
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request, [FromServices] Cheater cheater)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            
            // TODO support altcoins, not just bitcoin
            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(request.CryptoCode);
            var paymentMethodId = store.GetDefaultPaymentId() ?? store.GetEnabledPaymentIds(_NetworkProvider).FirstOrDefault(p => p.CryptoCode == request.CryptoCode && p.PaymentType == PaymentTypes.BTCLike);
            var bitcoinAddressString = invoice.GetPaymentMethod(paymentMethodId).GetPaymentMethodDetails().GetPaymentDestination();
            var bitcoinAddressObj = BitcoinAddress.Create(bitcoinAddressString, network.NBitcoinNetwork);
            var BtcAmount = request.Amount;
            
            try
            {
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var rate = paymentMethod.Rate;
                var txid = cheater.CashCow.SendToAddress(bitcoinAddressObj, new Money(BtcAmount, MoneyUnit.BTC)).ToString();
                
                // TODO The value of totalDue is wrong. How can we get the real total due? invoice.Price is only correct if this is the 2nd payment, not for a 3rd or 4th payment. 
                var totalDue = invoice.Price;
                return Ok(new
                {
                    Txid = txid,
                    AmountRemaining = (totalDue - (BtcAmount * rate)) / rate,
                    SuccessMessage = "Created transaction " + txid
                });
            }
            catch (Exception e)
            {
                return BadRequest(new
                {
                    ErrorMessage = e.Message,
                    AmountRemaining = invoice.Price
                });
            }
        }

        [HttpPost]
        [Route("i/{invoiceId}/expire")]
        [CheatModeRoute]
        public async Task<IActionResult> TestExpireNow(string invoiceId, [FromServices] Cheater cheater)
        {
            try
            {
                await cheater.UpdateInvoiceExpiry(invoiceId, DateTimeOffset.Now);
                return Ok(new { SuccessMessage = "Invoice is now expired." });
            }
            catch (Exception e)
            {
                return BadRequest(new { ErrorMessage = e.Message });
            }
        }
    }
}
