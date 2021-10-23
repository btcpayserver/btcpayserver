using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Invoices.Export;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.RPC;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;
using BitpayCreateInvoiceRequest = BTCPayServer.Models.BitpayCreateInvoiceRequest;
using StoreData = BTCPayServer.Data.StoreData;
using BTCPayServer.Services;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        public class FakePaymentRequest
        {
            public Decimal Amount { get; set; }
        }
        [HttpPost]
        [Route("i/{invoiceId}/test-payment")]
        [CheatModeRoute]
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request, [FromServices] Cheater cheater)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            
            // TODO support altcoins, not just bitcoin
            //var network = invoice.Networks.GetNetwork(invoice.Currency);
            var cryptoCode = "BTC";
            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var paymentMethodId = store.GetDefaultPaymentId();

            //var network = NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
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
