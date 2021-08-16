using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Crypto;
using Newtonsoft.Json;

namespace BTCPayServer
{
    [Route("~/{cryptoCode}/[controller]/")]
    public class LNURLController : Controller
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly InvoiceController _invoiceController;

        public LNURLController(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider btcPayNetworkProvider,
            LightningLikePaymentHandler lightningLikePaymentHandler,
            StoreRepository storeRepository,
            AppService appService,
            InvoiceController invoiceController)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningLikePaymentHandler = lightningLikePaymentHandler;
            _storeRepository = storeRepository;
            _appService = appService;
            _invoiceController = invoiceController;
        }
        
        [HttpGet("pay/i/{invoiceId}")]
        public async Task<IActionResult> GetLNURLForInvoice(string invoiceId, string cryptoCode,
            [FromQuery] long? amount = null, string comment = null)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null || !network.SupportLightning)
            {
                return NotFound();
            }

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var i = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (i.Status == InvoiceStatusLegacy.New)
            {
                var isTopup = i.IsUnsetTopUp();
                var lnurlSupportedPaymentMethod =
                    i.GetSupportedPaymentMethod<LNURLPaySupportedPaymentMethod>(pmi).FirstOrDefault();
                if (lnurlSupportedPaymentMethod is null ||
                    (!isTopup && !lnurlSupportedPaymentMethod.EnableForStandardInvoices))
                {
                    return NotFound();
                }

                var lightningPaymentMethod = i.GetPaymentMethod(pmi);
                var accounting = lightningPaymentMethod.Calculate();
                var paymentMethodDetails =
                    lightningPaymentMethod.GetPaymentMethodDetails() as LNURLPayPaymentMethodDetails;
                if (paymentMethodDetails.LightningSupportedPaymentMethod is null)
                {
                    return NotFound();
                }

                var min = new LightMoney(isTopup ? 1m : accounting.Due.ToUnit(MoneyUnit.Satoshi),
                    LightMoneyUnit.Satoshi);
                var max = isTopup ? LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC) : min;

                List<string[]> lnurlMetadata = new List<string[]>();

                lnurlMetadata.Add(new[] { "text/plain", i.Id });

                var metadata = JsonConvert.SerializeObject(lnurlMetadata);
                if (amount.HasValue && (amount < min || amount > max))
                {
                    return BadRequest(new LNUrlStatusResponse
                    {
                        Status = "ERROR", Reason = "Amount is out of bounds."
                    });
                }

                if (amount.HasValue && string.IsNullOrEmpty(paymentMethodDetails.BOLT11) ||
                    paymentMethodDetails.GeneratedBoltAmount != amount)
                {
                    var client =
                        _lightningLikePaymentHandler.CreateLightningClient(
                            paymentMethodDetails.LightningSupportedPaymentMethod, network);
                    if (!string.IsNullOrEmpty(paymentMethodDetails.BOLT11))
                    {
                        try
                        {
                            await client.CancelInvoice(paymentMethodDetails.InvoiceId);
                        }
                        catch (Exception)
                        {
                            //not a fully supported option
                        }
                    }

                    var descriptionHash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(metadata)));
                    LightningInvoice invoice;
                    try
                    {
                        invoice = await client.CreateInvoice(new CreateInvoiceParams(amount.Value,
                            descriptionHash,
                            i.ExpirationTime.ToUniversalTime() - DateTimeOffset.UtcNow));
                        if (!BOLT11PaymentRequest.Parse(invoice.BOLT11, network.NBitcoinNetwork)
                            .VerifyDescriptionHash(metadata))
                        {
                            return BadRequest(new LNUrlStatusResponse
                            {
                                Status = "ERROR",
                                Reason = "Lightning node could not generate invoice with a VALID description hash"
                            });
                        }
                    }
                    catch (Exception)
                    {
                        return BadRequest(new LNUrlStatusResponse
                        {
                            Status = "ERROR",
                            Reason = "Lightning node could not generate invoice with description hash"
                        });
                    }

                    paymentMethodDetails.BOLT11 = invoice.BOLT11;
                    paymentMethodDetails.InvoiceId = invoice.Id;
                    paymentMethodDetails.GeneratedBoltAmount = new LightMoney(amount.Value);
                    if (lnurlSupportedPaymentMethod.LUD12Enabled)
                    {
                        paymentMethodDetails.ProvidedComment = comment;
                    }

                    lightningPaymentMethod.SetPaymentMethodDetails(paymentMethodDetails);
                    await _invoiceRepository.UpdateInvoicePaymentMethod(invoiceId, lightningPaymentMethod);

                    _eventAggregator.Publish(new InvoiceNewPaymentDetailsEvent(invoiceId,
                        paymentMethodDetails, pmi));
                    return Ok(new LNURLPayRequest.LNURLPayRequestCallbackResponse
                    {
                        Disposable = true, Routes = Array.Empty<string>(), Pr = paymentMethodDetails.BOLT11
                    });
                }

                if (amount.HasValue && paymentMethodDetails.GeneratedBoltAmount == amount)
                {
                    if (lnurlSupportedPaymentMethod.LUD12Enabled && paymentMethodDetails.ProvidedComment != comment)
                    {
                        paymentMethodDetails.ProvidedComment = comment;
                        lightningPaymentMethod.SetPaymentMethodDetails(paymentMethodDetails);
                        await _invoiceRepository.UpdateInvoicePaymentMethod(invoiceId, lightningPaymentMethod);
                    }

                    return Ok(new LNURLPayRequest.LNURLPayRequestCallbackResponse
                    {
                        Disposable = true, Routes = Array.Empty<string>(), Pr = paymentMethodDetails.BOLT11
                    });
                }

                if (amount is null)
                {
                    return Ok(new LNURLPayRequest
                    {
                        Tag = "payRequest",
                        MinSendable = min,
                        MaxSendable = max,
                        CommentAllowed = lnurlSupportedPaymentMethod.LUD12Enabled ? 2000 : 0,
                        Metadata = metadata,
                        Callback = new Uri(Request.GetCurrentUrl())
                    });
                }
            }

            return BadRequest(new LNUrlStatusResponse
            {
                Status = "ERROR", Reason = "Invoice not in a valid payable state"
            });
        }
    }
}
