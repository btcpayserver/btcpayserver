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
using BTCPayServer.Plugins.CoinSwitch;
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

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        [HttpPost]
        [Route("i/{invoiceId}/test-payment")]
        public async Task<IActionResult> TestPayment(string invoiceId, FakePaymentRequest request)
        {
            if (_NetworkProvider.NetworkType != ChainName.Regtest) return Conflict();
            
            var credentialString = "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3";
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            
            // TODO support altcoins, not just bitcoin
            //var network = invoice.Networks.GetNetwork(invoice.Currency);
            var cryptoCode = "BTC";
            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var ExplorerNode = new RPCClient(RPCCredentialString.Parse(credentialString), network.NBitcoinNetwork);
            var paymentMethodId = store.GetDefaultPaymentId(_NetworkProvider);

            //var network = NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var bitcoinAddressString = invoice.GetPaymentMethod(paymentMethodId).GetPaymentMethodDetails().GetPaymentDestination();
            
            var bitcoinAddressObj = BitcoinAddress.Create(bitcoinAddressString, network.NBitcoinNetwork);
            var BtcAmount = request.Amount;

            var FakePaymentResponse = new FakePaymentResponse();

            try
            {
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var rate = paymentMethod.Rate;
                
                FakePaymentResponse.Txid = ExplorerNode.SendToAddress(bitcoinAddressObj, new Money(BtcAmount, MoneyUnit.BTC)).ToString();
                
                // TODO The value of totalDue is wrong. How can we get the real total due? invoice.Price is only correct if this is the 2nd payment, not for a 3rd or 4th payment. 
                var totalDue = invoice.Price;
                
                FakePaymentResponse.AmountRemaining = (totalDue - (BtcAmount * rate)) / rate;
                FakePaymentResponse.SuccessMessage = "Created transaction " + FakePaymentResponse.Txid;
            }
            catch (Exception e)
            {
                FakePaymentResponse.ErrorMessage = e.Message;
                FakePaymentResponse.AmountRemaining = invoice.Price;
            }

            if (FakePaymentResponse.Txid != null)
            {
                return Ok(FakePaymentResponse);                
            }
            return BadRequest(FakePaymentResponse);
        }

        [HttpPost]
        [Route("i/{invoiceId}/expire")]
        public async Task<IActionResult> TestExpireNow(string invoiceId)
        {
            if (_NetworkProvider.NetworkType != ChainName.Regtest) return Conflict();
            
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            ExpireInvoiceResponse expireInvoiceResponse = new ExpireInvoiceResponse();
            
            // TODO complete this
            try
            {
                await _InvoiceRepository.UpdateInvoiceExpiry(invoiceId, DateTimeOffset.Now);
                expireInvoiceResponse.SuccessMessage = "Invoice is now expired.";
            }
            catch (Exception e)
            {
                expireInvoiceResponse.ErrorMessage = e.Message;
            }
            
            if (expireInvoiceResponse.ErrorMessage == null)
            {
                return Ok(expireInvoiceResponse);
            }
            return BadRequest(expireInvoiceResponse);
        }
    }
}
