using System;
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
            public int BlockCount { get; set; } = 1;
            public string CryptoCode { get; set; } = "BTC";
        }

        [HttpPost("i/{invoiceId}/test-payment")]
        [CheatModeRoute]
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request, 
            [FromServices] Cheater cheater, 
            [FromServices] LightningClientFactoryService lightningClientFactoryService)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            var isSats = request.CryptoCode.ToUpper(CultureInfo.InvariantCulture) == "SATS";
            var cryptoCode = isSats ? "BTC" : request.CryptoCode;
            var amount = new Money(request.Amount, isSats ? MoneyUnit.Satoshi : MoneyUnit.BTC);
            var btcpayNetwork = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var network = btcpayNetwork.NBitcoinNetwork;
            var paymentMethodId = new[] { store.GetDefaultPaymentId() }
                .Concat(store.GetEnabledPaymentIds())
                .FirstOrDefault(p => p?.ToString() == request.PaymentMethodId);

            try
            {
                var paymentMethod = invoice.GetPaymentPrompt(paymentMethodId);
                var details = _handlers.ParsePaymentPromptDetails(paymentMethod);
                var destination = paymentMethod?.Destination;

                if (details is BitcoinPaymentPromptDetails)
                {
                    var address = BitcoinAddress.Create(destination, network);
                    var txid = (await cheater.GetCashCow(cryptoCode).SendToAddressAsync(address, amount)).ToString();

                    return Ok(new
                    {
                        Txid = txid,
                        AmountRemaining = paymentMethod.Calculate().Due - amount.ToDecimal(MoneyUnit.BTC),
                        SuccessMessage = $"Created transaction {txid}"
                    });
                }
                else if (details is LigthningPaymentPromptDetails)
                {
                    // requires the channels to be set up using the BTCPayServer.Tests/docker-lightning-channel-setup.sh script
                    var lnClient = lightningClientFactoryService.Create(
                        Environment.GetEnvironmentVariable("BTCPAY_BTCEXTERNALLNDREST"),
                        btcpayNetwork);

                    var lnAmount = new LightMoney(amount.Satoshi, LightMoneyUnit.Satoshi);
                    var response = await lnClient.Pay(destination, new PayInvoiceParams { Amount = lnAmount });

                    if (response.Result == PayResult.Ok)
                    {
                        var bolt11 = BOLT11PaymentRequest.Parse(destination, network);
                        var paymentHash = bolt11.PaymentHash?.ToString();
                        var paid = response.Details.TotalAmount.ToDecimal(LightMoneyUnit.BTC);
                        return Ok(new
                        {
                            Txid = paymentHash,
                            AmountRemaining = paymentMethod.Calculate().TotalDue - paid,
                            SuccessMessage = $"Sent payment {paymentHash}"
                        });
                    }
                    return UnprocessableEntity(new
                    {
                        ErrorMessage = response.ErrorDetail
                    });
                }
                else
                {
                    return UnprocessableEntity(new
                    {
                        ErrorMessage = $"Payment method {paymentMethodId} is not supported"
                    });
                }
            }
            catch (Exception e)
            {
                return BadRequest(new
                {
                    ErrorMessage = e.Message
                });
            }
        }

        [HttpPost("i/{invoiceId}/mine-blocks")]
        [CheatModeRoute]
        public IActionResult MineBlock(string invoiceId, MineBlocksRequest request, [FromServices] Cheater cheater)
        {
            var blockRewardBitcoinAddress = cheater.GetCashCow(request.CryptoCode).GetNewAddress();
            try
            {
                if (request.BlockCount > 0)
                {
                    cheater.GetCashCow(request.CryptoCode).GenerateToAddress(request.BlockCount, blockRewardBitcoinAddress);
                    return Ok(new { SuccessMessage = $"Mined {request.BlockCount} block{(request.BlockCount == 1 ? "" : "s")} " });
                }
                return BadRequest(new { ErrorMessage = "Number of blocks should be at least 1" });
            }
            catch (Exception e)
            {
                return BadRequest(new { ErrorMessage = e.Message });
            }
        }

        [HttpPost("i/{invoiceId}/expire")]
        [CheatModeRoute]
        public async Task<IActionResult> Expire(string invoiceId, int seconds, [FromServices] Cheater cheater)
        {
            try
            {
                await cheater.UpdateInvoiceExpiry(invoiceId, TimeSpan.FromSeconds(seconds));
                return Ok(new { SuccessMessage = $"Invoice set to expire in {seconds} seconds." });
            }
            catch (Exception e)
            {
                return BadRequest(new { ErrorMessage = e.Message });
            }
        }
    }
}
