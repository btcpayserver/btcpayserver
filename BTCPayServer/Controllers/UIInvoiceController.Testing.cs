using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Controllers
{
    public partial class UIInvoiceController
    {
        public class FakePaymentRequest
        {
            public Decimal Amount { get; set; }
            public string CryptoCode { get; set; } = "BTC";
            public string PaymentMethodId { get; set; } = "BTC";
        }

        public class MineBlocksRequest
        {
            public string PaymentMethodId { get; set; }
            public int BlockCount { get; set; } = 1;
            public string CryptoCode { get; set; } = "BTC";
        }

        [HttpPost("i/{invoiceId}/test-payment")]
        [CheatModeRoute]
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request,
            [FromServices] IEnumerable<ICheckoutCheatModeExtension> extensions)
        {
            var isSats = request.CryptoCode.ToUpper(CultureInfo.InvariantCulture) == "SATS";
            var amount = isSats ? new Money(request.Amount, MoneyUnit.Satoshi).ToDecimal(MoneyUnit.BTC) : request.Amount;
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            PaymentMethodId paymentMethodId = GetPaymentMethodId(request.PaymentMethodId, store);
            var paymentMethod = invoice.GetPaymentPrompt(paymentMethodId);
            var extension = GetCheatModeExtension(extensions, paymentMethodId);
            var details = _handlers.ParsePaymentPromptDetails(paymentMethod);
            if (extension is not null)
            {
                try
                {
                    var result = await extension.PayInvoice(new ICheckoutCheatModeExtension.PayInvoiceContext(
                        invoice,
                        amount,
                        store,
                        paymentMethod,
                        details));
                    return Ok(new
                    {
                        Txid = result.TransactionId,
                        AmountRemaining = result.AmountRemaining ?? paymentMethod.Calculate().Due - amount,
                        SuccessMessage = result.SuccessMessage ?? $"Created transaction {result.TransactionId}"
                    });
                }
                catch (Exception e)
                {
                    return BadRequest(new
                    {
                        ErrorMessage = e.Message
                    });
                }
            }
            else
            {
                return BadRequest(new { ErrorMessage = "No ICheatModeExtension registered for this payment method" });
            }
        }

        private static ICheckoutCheatModeExtension GetCheatModeExtension(IEnumerable<ICheckoutCheatModeExtension> extensions, PaymentMethodId paymentMethodId)
        {
            return extensions.Where(e => e.Handle(paymentMethodId)).FirstOrDefault();
        }

        private static PaymentMethodId GetPaymentMethodId(string requestPmi, StoreData store)
        {
            return new[] { store.GetDefaultPaymentId() }
                .Concat(store.GetEnabledPaymentIds())
                .FirstOrDefault(p => p?.ToString() == requestPmi);
        }

        [HttpPost("i/{invoiceId}/mine-blocks")]
        [CheatModeRoute]
        public async Task<IActionResult> MineBlock(string invoiceId, MineBlocksRequest request, [FromServices] IEnumerable<ICheckoutCheatModeExtension> extensions)
        {
            if (request.BlockCount <= 0)
                return BadRequest(new { ErrorMessage = "Number of blocks should be at least 1" });
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            var paymentMethodId = GetPaymentMethodId(request.PaymentMethodId, store);
            var extension = GetCheatModeExtension(extensions, paymentMethodId);
            if (extension != null)
            {
                try
                {
                    var result = await extension.MineBlock(new() { BlockCount = request.BlockCount });
                    var defaultMessage = $"Mined {request.BlockCount} block{(request.BlockCount == 1 ? "" : "s")} ";
                    return Ok(new { SuccessMessage = result.SuccessMessage ?? defaultMessage });
                }
                catch (Exception e)
                {
                    return BadRequest(new { ErrorMessage = e.Message });
                }
            }
            else
                return BadRequest(new { ErrorMessage = "No ICheatModeExtension registered for this payment method" });
        }

        [HttpPost("i/{invoiceId}/expire")]
        [CheatModeRoute]
        public async Task<IActionResult> Expire(string invoiceId, int seconds)
        {
            try
            {
                await _InvoiceRepository.UpdateInvoiceExpiry(invoiceId, TimeSpan.FromSeconds(seconds));
                return Ok(new { SuccessMessage = $"Invoice set to expire in {seconds} seconds." });
            }
            catch (Exception e)
            {
                return BadRequest(new { ErrorMessage = e.Message });
            }
        }
    }
}
