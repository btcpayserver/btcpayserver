using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
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
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request, [FromServices] Cheater cheater)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            var isSats = request.CryptoCode.ToUpper(CultureInfo.InvariantCulture) == "SATS";
            var cryptoCode = isSats ? "BTC" : request.CryptoCode;
            var amount = new Money(request.Amount, isSats ? MoneyUnit.Satoshi : MoneyUnit.BTC);
            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode).NBitcoinNetwork;
            var paymentMethodId = new [] {store.GetDefaultPaymentId()}
                .Concat(store.GetEnabledPaymentIds(_NetworkProvider))
                .FirstOrDefault(p => p?.ToString() == request.PaymentMethodId);
            
            try
            {
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var destination = paymentMethod?.GetPaymentMethodDetails().GetPaymentDestination();
                
                switch (paymentMethod?.GetId().PaymentType)
                {
                    case BitcoinPaymentType:
                        var address = BitcoinAddress.Create(destination, network);
                        var txid = (await cheater.CashCow.SendToAddressAsync(address, amount)).ToString();
                        
                        return Ok(new
                        {
                            Txid = txid,
                            AmountRemaining = (paymentMethod.Calculate().Due - amount).ToUnit(MoneyUnit.BTC),
                            SuccessMessage = $"Created transaction {txid}" 
                        });

                    case LightningPaymentType:
                        // requires the channels to be set up using the BTCPayServer.Tests/docker-lightning-channel-setup.sh script
                        LightningConnectionString.TryParse(Environment.GetEnvironmentVariable("BTCPAY_BTCEXTERNALLNDREST"), false, out var lnConnection);
                        var lnClient = LightningClientFactory.CreateClient(lnConnection, network);
                        var lnAmount = new LightMoney(amount.Satoshi, LightMoneyUnit.Satoshi);
                        var response = await lnClient.Pay(destination, new PayInvoiceParams { Amount = lnAmount });

                        if (response.Result == PayResult.Ok)
                        {
                            var bolt11 = BOLT11PaymentRequest.Parse(destination, network);
                            var paymentHash = bolt11.PaymentHash?.ToString();
                            var paid = new Money(response.Details.TotalAmount.ToUnit(LightMoneyUnit.Satoshi), MoneyUnit.Satoshi);
                            return Ok(new
                            {
                                Txid = paymentHash,
                                AmountRemaining = (paymentMethod.Calculate().TotalDue - paid).ToUnit(MoneyUnit.BTC),
                                SuccessMessage = $"Sent payment {paymentHash}" 
                            });
                        }
                        return UnprocessableEntity(new
                        {
                            ErrorMessage = response.ErrorDetail,
                            AmountRemaining = invoice.Price
                        });

                    default:
                        return UnprocessableEntity(new
                        {
                            ErrorMessage = $"Payment method {paymentMethodId} is not supported",
                            AmountRemaining = invoice.Price
                        });
                }
                
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

        [HttpPost("i/{invoiceId}/mine-blocks")]
        [CheatModeRoute]
        public IActionResult MineBlock(string invoiceId, MineBlocksRequest request, [FromServices] Cheater cheater)
        {
            var blockRewardBitcoinAddress = cheater.CashCow.GetNewAddress();
            try
            {
                if (request.BlockCount > 0)
                {
                    cheater.CashCow.GenerateToAddress(request.BlockCount, blockRewardBitcoinAddress);
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
