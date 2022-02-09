using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.LNbank.Authentication;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Controllers.API
{
    public class LightningController : BaseApiController
    {
        private readonly BTCPayService _btcpayService;
        private readonly WalletService _walletService;

        public LightningController(
            BTCPayService btcpayService,
            WalletService walletService,
            IOptionsMonitor<IdentityOptions> identityOptions) : base(identityOptions)
        {
            _btcpayService = btcpayService;
            _walletService = walletService;
        }

        // --- Custom methods ---

        [HttpPost("invoice")]
        public async Task<ActionResult<LightningInvoiceData>> CreateLightningInvoice(LightningInvoiceCreateRequest req)
        {
            if (Wallet == null) return NotFound();

            Transaction transaction;
            if (req.Description is null)
            {
                transaction = await _walletService.Receive(Wallet, req.Amount, req.DescriptionHash, req.PrivateRouteHints);
            }
            else
            {
                transaction = await _walletService.Receive(Wallet, req.Amount, req.Description, req.PrivateRouteHints);
            }
          
            var data = ToLightningInvoiceData(transaction);
            return Ok(data);
        }

        [HttpPost("pay")]
        public async Task<ActionResult<PayResponse>> Pay(LightningInvoicePayRequest req)
        {
            if (Wallet == null) return NotFound();

            var paymentRequest = req.PaymentRequest;
            var bolt11 = _walletService.ParsePaymentRequest(paymentRequest);

            try
            {
                await _walletService.Send(Wallet, bolt11, paymentRequest);
                var response = new PayResponse(PayResult.Ok);
                return Ok(response);
            }
            catch (Exception exception)
            {
                return new PayResponse(PayResult.Error, exception.Message);
            }
        }

        // ---- General methods ---

        [HttpGet("info")]
        public async Task<ActionResult<LightningNodeInformationData>> GetLightningNodeInfo()
        {
            var info = await _btcpayService.GetLightningNodeInfo();
            return Ok(info);
        }

        [HttpGet("invoice/{invoiceId}")]
        public async Task<ActionResult<LightningInvoiceData>> GetLightningInvoice(string invoiceId)
        {
            var transaction = await _walletService.GetTransaction(new TransactionQuery
            {
                UserId = UserId,
                WalletId = WalletId,
                InvoiceId = invoiceId
            });
            var invoice = ToLightningInvoiceData(transaction);
            return Ok(invoice);
        }

        [HttpDelete("invoice/{invoiceId}")]
        public async Task<ActionResult<LightningInvoiceData>> CancelLightningInvoice(string invoiceId)
        {
            await _walletService.Cancel(invoiceId);
            return Ok();
        }

        [HttpGet("channels")]
        public async Task<ActionResult<IEnumerable<LightningChannelData>>> ListLightningChannels()
        {
            var list = await _btcpayService.ListLightningChannels();
            return Ok(list);
        }

        [HttpPost("channels")]
        public async Task<ActionResult<string>> OpenLightningChannel(OpenLightningChannelRequest req)
        {
            await _btcpayService.OpenLightningChannel(req);
            return Ok();
        }

        [HttpPost("connect")]
        public async Task<ActionResult> ConnectToLightningNode(ConnectToNodeRequest req)
        {
            await _btcpayService.ConnectToLightningNode(req);
            return Ok();
        }

        [HttpPost("deposit-address")]
        public async Task<ActionResult<string>> GetLightningDepositAddress()
        {
            var address = await _btcpayService.GetLightningDepositAddress();
            return Ok(address);
        }

        private LightningInvoiceData ToLightningInvoiceData(Transaction transaction) =>
            new LightningInvoiceData
            {
                Amount = transaction.Amount,
                Id = transaction.InvoiceId,
                Status = transaction.LightningInvoiceStatus,
                AmountReceived = transaction.AmountSettled,
                PaidAt = transaction.PaidAt,
                BOLT11 = transaction.PaymentRequest,
                ExpiresAt = transaction.ExpiresAt
            };
    }
}
