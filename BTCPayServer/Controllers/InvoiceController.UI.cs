#nullable enable
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
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Invoices.Export;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;
using BitpayCreateInvoiceRequest = BTCPayServer.Models.BitpayCreateInvoiceRequest;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {

        [HttpGet]
        [Route("invoices/{invoiceId}/deliveries/{deliveryId}/request")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> WebhookDelivery(string invoiceId, string deliveryId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId()
            })).FirstOrDefault();
            if (invoice is null)
                return NotFound();
            var delivery = await _InvoiceRepository.GetWebhookDelivery(invoiceId, deliveryId);
            if (delivery is null)
                return NotFound();
            return this.File(delivery.GetBlob().Request, "application/json");
        }
        [HttpPost]
        [Route("invoices/{invoiceId}/deliveries/{deliveryId}/redeliver")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> RedeliverWebhook(string storeId, string invoiceId, string deliveryId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { invoiceId },
                StoreId = new[] { storeId },
                UserId = GetUserId()
            })).FirstOrDefault();
            if (invoice is null)
                return NotFound();
            var delivery = await _InvoiceRepository.GetWebhookDelivery(invoiceId, deliveryId);
            if (delivery is null)
                return NotFound();
            var newDeliveryId = await WebhookNotificationManager.Redeliver(deliveryId);
            if (newDeliveryId is null)
                return NotFound();
            TempData[WellKnownTempData.SuccessMessage] = "Successfully planned a redelivery";
            return RedirectToAction(nameof(Invoice),
                new
                {
                    invoiceId
                });
        }

        [HttpGet]
        [Route("invoices/{invoiceId}")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Invoice(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId(),
                IncludeAddresses = true,
                IncludeEvents = true,
                IncludeArchived = true,
            })).FirstOrDefault();
            if (invoice == null)
                return NotFound();

            var store = await _StoreRepository.FindStore(invoice.StoreId);
            var invoiceState = invoice.GetInvoiceState();
            var model = new InvoiceDetailsModel()
            {
                StoreId = store.Id,
                StoreName = store.StoreName,
                StoreLink = Url.Action(nameof(StoresController.PaymentMethods), "Stores", new { storeId = store.Id }),
                PaymentRequestLink = Url.Action(nameof(PaymentRequestController.ViewPaymentRequest), "PaymentRequest", new { id = invoice.Metadata.PaymentRequestId }),
                Id = invoice.Id,
                State = invoiceState.ToString(),
                TransactionSpeed = invoice.SpeedPolicy == SpeedPolicy.HighSpeed ? "high" :
                                   invoice.SpeedPolicy == SpeedPolicy.MediumSpeed ? "medium" :
                                   invoice.SpeedPolicy == SpeedPolicy.LowMediumSpeed ? "low-medium" :
                                   "low",
                RefundEmail = invoice.RefundMail,
                CreatedDate = invoice.InvoiceTime,
                ExpirationDate = invoice.ExpirationTime,
                MonitoringDate = invoice.MonitoringExpiration,
                Fiat = _CurrencyNameTable.DisplayFormatCurrency(invoice.Price, invoice.Currency),
                TaxIncluded = _CurrencyNameTable.DisplayFormatCurrency(invoice.Metadata.TaxIncluded ?? 0.0m, invoice.Currency),
                NotificationUrl = invoice.NotificationURL?.AbsoluteUri,
                RedirectUrl = invoice.RedirectURL?.AbsoluteUri,
                TypedMetadata = invoice.Metadata,
                StatusException = invoice.ExceptionStatus,
                Events = invoice.Events,
                PosData = PosDataParser.ParsePosData(invoice.Metadata.PosData),
                Archived = invoice.Archived,
                CanRefund = CanRefund(invoiceState),
                ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
                Deliveries = (await _InvoiceRepository.GetWebhookDeliveries(invoiceId))
                                    .Select(c => new Models.StoreViewModels.DeliveryViewModel(c))
                                    .ToList(),
                CanMarkInvalid = invoiceState.CanMarkInvalid(),
                CanMarkComplete = invoiceState.CanMarkComplete(),
            };
            model.Addresses = invoice.HistoricalAddresses.Select(h =>
                new InvoiceDetailsModel.AddressModel
                {
                    Destination = h.GetAddress(),
                    PaymentMethod = h.GetPaymentMethodId().ToPrettyString(),
                    Current = !h.UnAssigned.HasValue
                }).ToArray();

            var details = InvoicePopulatePayments(invoice);
            model.CryptoPayments = details.CryptoPayments;
            model.Payments = details.Payments;

            return View(model);
        }

        bool CanRefund(InvoiceState invoiceState)
        {
            return invoiceState.Status == InvoiceStatusLegacy.Confirmed ||
                invoiceState.Status == InvoiceStatusLegacy.Complete ||
                (invoiceState.Status == InvoiceStatusLegacy.Expired &&
                (invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidLate ||
                invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidOver ||
                invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)) ||
                invoiceState.Status == InvoiceStatusLegacy.Invalid;
        }

        [HttpGet]
        [Route("invoices/{invoiceId}/refund")]
        [AllowAnonymous]
        public async Task<IActionResult> Refund([FromServices]IEnumerable<IPayoutHandler> payoutHandlers, string invoiceId, CancellationToken cancellationToken)
        {
            using var ctx = _dbContextFactory.CreateContext();
            ctx.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            var invoice = await ctx.Invoices.Include(i => i.Payments)
                                            .Include(i => i.CurrentRefund)
                                            .Include(i => i.CurrentRefund.PullPaymentData)
                                            .Where(i => i.Id == invoiceId)
                                            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
            if (invoice is null)
                return NotFound();
            if (invoice.CurrentRefund?.PullPaymentDataId is null && GetUserId() is null)
                return NotFound();
            if (!CanRefund(invoice.GetInvoiceState()))
                return NotFound();
            if (invoice.CurrentRefund?.PullPaymentDataId is string ppId && !invoice.CurrentRefund.PullPaymentData.Archived)
            {
                // TODO: Having dedicated UI later on
                return RedirectToAction(nameof(PullPaymentController.ViewPullPayment),
                                "PullPayment",
                                new { pullPaymentId = ppId });
            }
            else
            {
                var paymentMethods = invoice.GetBlob(_NetworkProvider).GetPaymentMethods();
                var pmis = paymentMethods.Select(method => method.GetId()).ToList();
                var options = payoutHandlers.GetSupportedPaymentMethods(pmis);
                var defaultRefund = invoice.Payments
                    .Select(p => p.GetBlob(_NetworkProvider))
                    .Select(p => p?.GetPaymentMethodId())
                    .FirstOrDefault(p => p != null && options.Contains(p));
                // TODO: What if no option?
                var refund = new RefundModel();
                refund.Title = "Select a payment method";
                refund.AvailablePaymentMethods = 
                    new SelectList(options.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString())), "Value", "Text");
                refund.SelectedPaymentMethod = defaultRefund?.ToString() ?? options.First().ToString();

                // Nothing to select, skip to next
                if (refund.AvailablePaymentMethods.Count() == 1)
                {
                    return await Refund(invoiceId, refund, cancellationToken);
                }
                return View(refund);
            }
        }
        [HttpPost]
        [Route("invoices/{invoiceId}/refund")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund(string invoiceId, RefundModel model, CancellationToken cancellationToken)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice is null)
                return NotFound();
            var store = await _StoreRepository.FindStore(invoice.StoreId, GetUserId());
            if (store is null)
                return NotFound();
            if (!CanRefund(invoice.GetInvoiceState()))
                return NotFound();
            var paymentMethodId = PaymentMethodId.Parse(model.SelectedPaymentMethod);
            var cdCurrency = _CurrencyNameTable.GetCurrencyData(invoice.Currency, true);
            var paymentMethodDivisibility = _CurrencyNameTable.GetCurrencyData(paymentMethodId.CryptoCode, false)?.Divisibility ?? 8;
            RateRules rules;
            RateResult rateResult;
            CreatePullPayment createPullPayment;
            switch (model.RefundStep)
            {
                case RefundSteps.SelectPaymentMethod:
                    model.RefundStep = RefundSteps.SelectRate;
                    model.Title = "What to refund?";
                    var pms = invoice.GetPaymentMethods();
                    var paymentMethod = pms.SingleOrDefault(method => method.GetId() == paymentMethodId);
                    
                    //TODO: Make this clean
                    if (paymentMethod is null && paymentMethodId.PaymentType == LightningPaymentType.Instance)
                    {
                        paymentMethod = pms[new PaymentMethodId(paymentMethodId.CryptoCode, PaymentTypes.LNURLPay)];
                    }
                    var cryptoPaid = paymentMethod.Calculate().Paid.ToDecimal(MoneyUnit.BTC);
                    var paidCurrency =
                        Math.Round(cryptoPaid * paymentMethod.Rate,
                            cdCurrency.Divisibility);
                    model.CryptoAmountThen = cryptoPaid.RoundToSignificant(paymentMethodDivisibility);
                    model.RateThenText =
                        _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountThen, paymentMethodId.CryptoCode);
                    rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
                    rateResult = await _RateProvider.FetchRate(
                        new Rating.CurrencyPair(paymentMethodId.CryptoCode, invoice.Currency), rules,
                        cancellationToken);
                    //TODO: What if fetching rate failed?
                    if (rateResult.BidAsk is null)
                    {
                        ModelState.AddModelError(nameof(model.SelectedRefundOption),
                            $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                        return View(model);
                    }

                    model.CryptoAmountNow = Math.Round(paidCurrency / rateResult.BidAsk.Bid, paymentMethodDivisibility);
                    model.CurrentRateText =
                        _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountNow, paymentMethodId.CryptoCode);
                    model.FiatAmount = paidCurrency;
                    model.FiatText = _CurrencyNameTable.DisplayFormatCurrency(model.FiatAmount, invoice.Currency);
                    return View(model);
                case RefundSteps.SelectRate:
                    createPullPayment = new HostedServices.CreatePullPayment();
                createPullPayment.Name = $"Refund {invoice.Id}";
                createPullPayment.PaymentMethodIds = new[] { paymentMethodId };
                createPullPayment.StoreId = invoice.StoreId;
                switch (model.SelectedRefundOption)
                {
                    case "RateThen":
                        createPullPayment.Currency = paymentMethodId.CryptoCode;
                        createPullPayment.Amount = model.CryptoAmountThen;
                        break;
                    case "CurrentRate":
                        createPullPayment.Currency = paymentMethodId.CryptoCode;
                        createPullPayment.Amount = model.CryptoAmountNow;
                        break;
                    case "Fiat":
                        createPullPayment.Currency = invoice.Currency;
                        createPullPayment.Amount = model.FiatAmount;
                        break;
                    case "Custom":
                        model.Title = "How much to refund?";
                        model.CustomCurrency = invoice.Currency;
                        model.CustomAmount = model.FiatAmount;
                        model.RefundStep = RefundSteps.SelectCustomAmount;
                        return View(model);
                    default:
                        ModelState.AddModelError(nameof(model.SelectedRefundOption), "Invalid choice");
                        return View(model);
                }

                    break;
                case RefundSteps.SelectCustomAmount:
                    if (model.CustomAmount <= 0)
                    {
                        model.AddModelError(refundModel => refundModel.CustomAmount, "Amount must be greater than 0", this);
                    }

                    if (string.IsNullOrEmpty(model.CustomCurrency) ||
                        _CurrencyNameTable.GetCurrencyData(model.CustomCurrency, false) == null)
                    {
                        ModelState.AddModelError(nameof(model.CustomCurrency), "Invalid currency");
                    }

                    if (!ModelState.IsValid)
                    {
                        return View(model);
                    }
                    rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
                    rateResult = await _RateProvider.FetchRate(
                        new Rating.CurrencyPair(paymentMethodId.CryptoCode, model.CustomCurrency), rules,
                        cancellationToken);
                    //TODO: What if fetching rate failed?
                    if (rateResult.BidAsk is null)
                    {
                        ModelState.AddModelError(nameof(model.SelectedRefundOption),
                            $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                        return View(model);
                    }

                    createPullPayment = new HostedServices.CreatePullPayment();
                    createPullPayment.Name = $"Refund {invoice.Id}";
                    createPullPayment.PaymentMethodIds = new[] { paymentMethodId };
                    createPullPayment.StoreId = invoice.StoreId;
                    createPullPayment.Currency = model.CustomCurrency;
                    createPullPayment.Amount = model.CustomAmount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var ppId = await _paymentHostedService.CreatePullPayment(createPullPayment);
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Html = "Refund successfully created!<br />Share the link to this page with a customer.<br />The customer needs to enter their address and claim the refund.<br />Once a customer claims the refund, you will get a notification and would need to approve and initiate it from your Store > Payouts.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            (await ctx.Invoices.FindAsync(new[] { invoice.Id }, cancellationToken: cancellationToken)).CurrentRefundId = ppId;
            ctx.Refunds.Add(new RefundData()
            {
                InvoiceDataId = invoice.Id,
                PullPaymentDataId = ppId
            });
            await ctx.SaveChangesAsync(cancellationToken);
            // TODO: Having dedicated UI later on
            return RedirectToAction(nameof(PullPaymentController.ViewPullPayment),
                "PullPayment",
                new { pullPaymentId = ppId });
        }

        private InvoiceDetailsModel InvoicePopulatePayments(InvoiceEntity invoice)
        {
            return new InvoiceDetailsModel
            {
                Archived = invoice.Archived,
                Payments = invoice.GetPayments(false),
                CryptoPayments = invoice.GetPaymentMethods().Select(
                    data =>
                    {
                        var accounting = data.Calculate();
                        var paymentMethodId = data.GetId();
                        return new InvoiceDetailsModel.CryptoPayment
                        {
                            PaymentMethodId = paymentMethodId,
                            PaymentMethod = paymentMethodId.ToPrettyString(),
                            Due = _CurrencyNameTable.DisplayFormatCurrency(accounting.Due.ToDecimal(MoneyUnit.BTC),
                                paymentMethodId.CryptoCode),
                            Paid = _CurrencyNameTable.DisplayFormatCurrency(
                                accounting.CryptoPaid.ToDecimal(MoneyUnit.BTC),
                                paymentMethodId.CryptoCode),
                            Overpaid = _CurrencyNameTable.DisplayFormatCurrency(
                                accounting.OverpaidHelper.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode),
                            Address = data.GetPaymentMethodDetails().GetPaymentDestination(),
                            Rate = ExchangeRate(data),
                            PaymentMethodRaw = data
                        };
                    }).ToList()
            };
        }

        [HttpPost("invoices/{invoiceId}/archive")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ToggleArchive(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId(),
                IncludeAddresses = true,
                IncludeEvents = true,
                IncludeArchived = true,
            })).FirstOrDefault();
            if (invoice == null)
                return NotFound();
            await _InvoiceRepository.ToggleInvoiceArchival(invoiceId, !invoice.Archived);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = invoice.Archived ? "The invoice has been unarchived and will appear in the invoice list by default again." : "The invoice has been archived and will no longer appear in the invoice list by default."
            });
            return RedirectToAction(nameof(invoice), new { invoiceId });
        }

        [HttpPost]
        public async Task<IActionResult> MassAction(string command, string[] selectedItems)
        {
            if (selectedItems != null)
            {
                switch (command)
                {
                    case "archive":
                        await _InvoiceRepository.MassArchive(selectedItems);
                        TempData[WellKnownTempData.SuccessMessage] = $"{selectedItems.Length} invoice(s) archived.";

                        break;
                }
            }

            return RedirectToAction(nameof(ListInvoices));
        }

        [HttpGet]
        [Route("i/{invoiceId}")]
        [Route("i/{invoiceId}/{paymentMethodId}")]
        [Route("invoice")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest", false)]
        [XFrameOptionsAttribute(null)]
        [ReferrerPolicyAttribute("origin")]
        public async Task<IActionResult> Checkout(string? invoiceId, string? id = null, string? paymentMethodId = null,
            [FromQuery] string? view = null, [FromQuery] string? lang = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            //
            if (invoiceId is null)
                return NotFound();
            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId), lang);
            if (model == null)
                return NotFound();

            if (view == "modal")
                model.IsModal = true;
            return View(nameof(Checkout), model);
        }
        
        [HttpGet]
        [Route("invoice-noscript")]
        public async Task<IActionResult> CheckoutNoScript(string? invoiceId, string? id = null, string? paymentMethodId = null, [FromQuery] string? lang = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            //
            if (invoiceId is null)
                return NotFound();
            var model = await GetInvoiceModel(invoiceId, paymentMethodId is null ? null : PaymentMethodId.Parse(paymentMethodId), lang);
            if (model == null)
                return NotFound();

            return View(model);
        }

        private async Task<PaymentModel?> GetInvoiceModel(string invoiceId, PaymentMethodId? paymentMethodId, string? lang)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice == null)
                return null;
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            bool isDefaultPaymentId = false;
            if (paymentMethodId is null)
            {
                var enabledPaymentIds = store.GetEnabledPaymentIds(_NetworkProvider) ?? Array.Empty<PaymentMethodId>();
                PaymentMethodId? invoicePaymentId = invoice.GetDefaultPaymentMethod();
                PaymentMethodId? storePaymentId = store.GetDefaultPaymentId();
                if (invoicePaymentId is PaymentMethodId)
                {
                    if (enabledPaymentIds.Contains(invoicePaymentId))
                        paymentMethodId = invoicePaymentId;
                }
                if (paymentMethodId is null && storePaymentId is PaymentMethodId)
                {
                    if (enabledPaymentIds.Contains(storePaymentId))
                        paymentMethodId = storePaymentId;
                }
                if (paymentMethodId is null && invoicePaymentId is PaymentMethodId)
                {
                    paymentMethodId = invoicePaymentId.FindNearest(enabledPaymentIds);
                }
                if (paymentMethodId is null && storePaymentId is PaymentMethodId)
                {
                    paymentMethodId = storePaymentId.FindNearest(enabledPaymentIds);
                }
                if (paymentMethodId is null)
                {
                    paymentMethodId = enabledPaymentIds.First();
                }
                isDefaultPaymentId = true;
            }
            BTCPayNetworkBase network = _NetworkProvider.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
            if (network is null || !invoice.Support(paymentMethodId))
            {
                if (!isDefaultPaymentId)
                    return null;
                var paymentMethodTemp = invoice
                    .GetPaymentMethods()
                    .FirstOrDefault(c => paymentMethodId.CryptoCode == c.GetId().CryptoCode);
                if (paymentMethodTemp == null)
                    paymentMethodTemp = invoice.GetPaymentMethods().First();
                network = paymentMethodTemp.Network;
                paymentMethodId = paymentMethodTemp.GetId();
            }

            var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
            var paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
            if (!paymentMethodDetails.Activated)
            {
                if (await _InvoiceRepository.ActivateInvoicePaymentMethod(_EventAggregator, _NetworkProvider,
                    _paymentMethodHandlerDictionary, store, invoice, paymentMethod.GetId()))
                {
                    return await GetInvoiceModel(invoiceId, paymentMethodId, lang);
                }
            }
            var dto = invoice.EntityToDTO();
            var storeBlob = store.GetStoreBlob();
            var accounting = paymentMethod.Calculate();

            var paymentMethodHandler = _paymentMethodHandlerDictionary[paymentMethodId];

            var divisibility = _CurrencyNameTable.GetNumberFormatInfo(paymentMethod.GetId().CryptoCode, false)?.CurrencyDecimalDigits;

            switch (lang?.ToLowerInvariant())
            {
                case "auto":
                case null when storeBlob.AutoDetectLanguage:
                    lang = _languageService.AutoDetectLanguageUsingHeader(HttpContext.Request.Headers, null).Code;
                    break;
                case { } langs when !string.IsNullOrEmpty(langs):
                {
                    lang = _languageService.FindLanguage(langs)?.Code;
                    break;
                }
            }
            lang ??= storeBlob.DefaultLang;

            var model = new PaymentModel()
            {
                Activated = paymentMethodDetails.Activated,
                CryptoCode = network.CryptoCode,
                RootPath = this.Request.PathBase.Value.WithTrailingSlash(),
                OrderId = invoice.Metadata.OrderId,
                InvoiceId = invoice.Id,
                DefaultLang = lang ?? invoice.DefaultLanguage ?? storeBlob.DefaultLang ?? "en",
                CustomCSSLink = storeBlob.CustomCSS,
                CustomLogoLink = storeBlob.CustomLogo,
                HtmlTitle = storeBlob.HtmlTitle ?? "BTCPay Invoice",
                CryptoImage = Request.GetRelativePathOrAbsolute(paymentMethodHandler.GetCryptoImage(paymentMethodId)),
                BtcAddress = paymentMethodDetails.GetPaymentDestination(),
                BtcDue = accounting.Due.ShowMoney(divisibility),
                InvoiceCurrency = invoice.Currency,
                OrderAmount = (accounting.TotalDue - accounting.NetworkFee).ShowMoney(divisibility),
                IsUnsetTopUp = invoice.IsUnsetTopUp(),
                OrderAmountFiat = OrderAmountFromInvoice(network.CryptoCode, invoice),
                CustomerEmail = invoice.RefundMail,
                RequiresRefundEmail = invoice.RequiresRefundEmail ?? storeBlob.RequiresRefundEmail,
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.Metadata.ItemDesc,
                Rate = ExchangeRate(paymentMethod),
                MerchantRefLink = invoice.RedirectURL?.AbsoluteUri ?? "/",
                RedirectAutomatically = invoice.RedirectAutomatically,
                StoreName = store.StoreName,
                TxCount = accounting.TxRequired,
                TxCountForFee = storeBlob.NetworkFeeMode switch
                {
                    NetworkFeeMode.Always => accounting.TxRequired,
                    NetworkFeeMode.MultiplePaymentsOnly => accounting.TxRequired - 1,
                    NetworkFeeMode.Never => 0,
                    _ => throw new NotImplementedException()
                },
                BtcPaid = accounting.Paid.ShowMoney(divisibility),
#pragma warning disable CS0618 // Type or member is obsolete
                Status = invoice.StatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                NetworkFee = paymentMethodDetails.GetNextNetworkFee(),
                IsMultiCurrency = invoice.GetPayments(false).Select(p => p.GetPaymentMethodId()).Concat(new[] { paymentMethod.GetId() }).Distinct().Count() > 1,
                StoreId = store.Id,
                AvailableCryptos = invoice.GetPaymentMethods()
                                          .Where(i => i.Network != null)
                                          .Select(kv =>
                                          {
                                              var availableCryptoPaymentMethodId = kv.GetId();
                                              var availableCryptoHandler = _paymentMethodHandlerDictionary[availableCryptoPaymentMethodId];
                                              return new PaymentModel.AvailableCrypto()
                                              {
                                                  PaymentMethodId = kv.GetId().ToString(),
                                                  CryptoCode = kv.Network?.CryptoCode ?? kv.GetId().CryptoCode,
                                                  PaymentMethodName = availableCryptoHandler.GetPaymentMethodName(availableCryptoPaymentMethodId),
                                                  IsLightning =
                                                      kv.GetId().PaymentType == PaymentTypes.LightningLike,
                                                  CryptoImage = Request.GetRelativePathOrAbsolute(availableCryptoHandler.GetCryptoImage(availableCryptoPaymentMethodId)),
                                                  Link = Url.Action(nameof(Checkout),
                                                      new
                                                      {
                                                          invoiceId = invoiceId,
                                                          paymentMethodId = kv.GetId().ToString()
                                                      })
                                              };
                                          }).Where(c => c.CryptoImage != "/")
                                          .OrderByDescending(a => a.CryptoCode == "BTC").ThenBy(a => a.PaymentMethodName).ThenBy(a => a.IsLightning ? 1 : 0)
                                          .ToList()
            };
            paymentMethodHandler.PreparePaymentModel(model, dto, storeBlob, paymentMethod);
            model.UISettings = paymentMethodHandler.GetCheckoutUISettings();
            model.PaymentMethodId = paymentMethodId.ToString();
            var expiration = TimeSpan.FromSeconds(model.ExpirationSeconds);
            model.TimeLeft = expiration.PrettyPrint();
            return model;
        }

        private string? OrderAmountFromInvoice(string cryptoCode, InvoiceEntity invoiceEntity)
        {
            // if invoice source currency is the same as currently display currency, no need for "order amount from invoice"
            if (cryptoCode == invoiceEntity.Currency)
                return null;

            return _CurrencyNameTable.DisplayFormatCurrency(invoiceEntity.Price, invoiceEntity.Currency);
        }
        private string ExchangeRate(PaymentMethod paymentMethod)
        {
            string currency = paymentMethod.ParentEntity.Currency;
            return _CurrencyNameTable.DisplayFormatCurrency(paymentMethod.Rate, currency);
        }

        [HttpGet]
        [Route("i/{invoiceId}/status")]
        [Route("i/{invoiceId}/{implicitPaymentMethodId}/status")]
        [Route("invoice/{invoiceId}/status")]
        [Route("invoice/{invoiceId}/{implicitPaymentMethodId}/status")]
        [Route("invoice/status")]
        public async Task<IActionResult> GetStatus(string invoiceId, string? paymentMethodId = null, string? implicitPaymentMethodId = null, [FromQuery] string? lang = null)
        {
            if (string.IsNullOrEmpty(paymentMethodId))
                paymentMethodId = implicitPaymentMethodId;
            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId), lang);
            if (model == null)
                return NotFound();
            return Json(model);
        }

        [HttpGet]
        [Route("i/{invoiceId}/status/ws")]
        [Route("i/{invoiceId}/{paymentMethodId}/status/ws")]
        [Route("invoice/{invoiceId}/status/ws")]
        [Route("invoice/{invoiceId}/{paymentMethodId}/status")]
        [Route("invoice/status/ws")]
        public async Task<IActionResult> GetStatusWebSocket(string invoiceId, CancellationToken cancellationToken)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice == null || invoice.Status == InvoiceStatusLegacy.Complete || invoice.Status == InvoiceStatusLegacy.Invalid || invoice.Status == InvoiceStatusLegacy.Expired)
                return NotFound();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            CompositeDisposable leases = new CompositeDisposable();
            try
            {
                leases.Add(_EventAggregator.SubscribeAsync<Events.InvoiceDataChangedEvent>(async o => await NotifySocket(webSocket, o.InvoiceId, invoiceId)));
                leases.Add(_EventAggregator.SubscribeAsync<Events.InvoiceNewPaymentDetailsEvent>(async o => await NotifySocket(webSocket, o.InvoiceId, invoiceId)));
                leases.Add(_EventAggregator.SubscribeAsync<Events.InvoiceEvent>(async o => await NotifySocket(webSocket, o.Invoice.Id, invoiceId)));
                while (true)
                {
                    var message = await webSocket.ReceiveAndPingAsync(DummyBuffer, default(CancellationToken));
                    if (message.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (WebSocketException) { }
            finally
            {
                leases.Dispose();
                await webSocket.CloseSocket();
            }
            return new EmptyResult();
        }

        readonly ArraySegment<Byte> DummyBuffer = new ArraySegment<Byte>(new Byte[1]);
        public string? CreatedInvoiceId;

        private async Task NotifySocket(WebSocket webSocket, string invoiceId, string expectedId)
        {
            if (invoiceId != expectedId || webSocket.State != WebSocketState.Open)
                return;
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            try
            {
                await webSocket.SendAsync(DummyBuffer, WebSocketMessageType.Binary, true, cts.Token);
            }
            catch { try { webSocket.Dispose(); } catch { } }
        }

        [HttpPost]
        [Route("i/{invoiceId}/UpdateCustomer")]
        [Route("invoice/UpdateCustomer")]
        public async Task<IActionResult> UpdateCustomer(string invoiceId, [FromBody] UpdateCustomerModel data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            await _InvoiceRepository.UpdateInvoice(invoiceId, data).ConfigureAwait(false);
            return Ok("{}");
        }

        [HttpGet]
        [Route("invoices")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ListInvoices(InvoicesModel? model = null)
        {
            model = this.ParseListQuery(model ?? new InvoicesModel());

            var fs = new SearchString(model.SearchTerm);
            var storeIds = fs.GetFilterArray("storeid") != null ? fs.GetFilterArray("storeid") : new List<string>().ToArray();

            model.StoreIds = storeIds;

            InvoiceQuery invoiceQuery = GetInvoiceQuery(model.SearchTerm, model.TimezoneOffset ?? 0);
            var counting = _InvoiceRepository.GetInvoicesTotal(invoiceQuery);
            invoiceQuery.Take = model.Count;
            invoiceQuery.Skip = model.Skip;
            var list = await _InvoiceRepository.GetInvoices(invoiceQuery);

            foreach (var invoice in list)
            {
                var state = invoice.GetInvoiceState();
                model.Invoices.Add(new InvoiceModel()
                {
                    Status = state,
                    ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
                    Date = invoice.InvoiceTime,
                    InvoiceId = invoice.Id,
                    OrderId = invoice.Metadata.OrderId ?? string.Empty,
                    RedirectUrl = invoice.RedirectURL?.AbsoluteUri ?? string.Empty,
                    AmountCurrency = _CurrencyNameTable.DisplayFormatCurrency(invoice.Price, invoice.Currency),
                    CanMarkInvalid = state.CanMarkInvalid(),
                    CanMarkComplete = state.CanMarkComplete(),
                    Details = InvoicePopulatePayments(invoice),
                });
            }
            model.Total = await counting;
            return View(model);
        }

        private InvoiceQuery GetInvoiceQuery(string? searchTerm = null, int timezoneOffset = 0)
        {
            var fs = new SearchString(searchTerm);
            var invoiceQuery = new InvoiceQuery()
            {
                TextSearch = fs.TextSearch,
                UserId = GetUserId(),
                Unusual = fs.GetFilterBool("unusual"),
                IncludeArchived = fs.GetFilterBool("includearchived") ?? false,
                Status = fs.GetFilterArray("status"),
                ExceptionStatus = fs.GetFilterArray("exceptionstatus"),
                StoreId = fs.GetFilterArray("storeid"),
                ItemCode = fs.GetFilterArray("itemcode"),
                OrderId = fs.GetFilterArray("orderid"),
                StartDate = fs.GetFilterDate("startdate", timezoneOffset),
                EndDate = fs.GetFilterDate("enddate", timezoneOffset)
            };
            return invoiceQuery;
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> Export(string format, string? searchTerm = null, int timezoneOffset = 0)
        {
            var model = new InvoiceExport(_CurrencyNameTable);

            InvoiceQuery invoiceQuery = GetInvoiceQuery(searchTerm, timezoneOffset);
            invoiceQuery.Skip = 0;
            invoiceQuery.Take = int.MaxValue;
            var invoices = await _InvoiceRepository.GetInvoices(invoiceQuery);
            var res = model.Process(invoices, format);

            var cd = new ContentDisposition
            {
                FileName = $"btcpay-export-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{format}",
                Inline = true
            };
            Response.Headers.Add("Content-Disposition", cd.ToString());
            Response.Headers.Add("X-Content-Type-Options", "nosniff");
            return Content(res, "application/" + format);
        }


        private SelectList GetPaymentMethodsSelectList()
        {
            return new SelectList(_paymentMethodHandlerDictionary.Distinct().SelectMany(handler =>
                    handler.GetSupportedPaymentMethods()
                        .Select(id => new SelectListItem(id.ToPrettyString(), id.ToString()))),
                nameof(SelectListItem.Value),
                nameof(SelectListItem.Text));
        }

        [HttpGet]
        [Route("invoices/create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(InvoicesModel? model = null)
        {
            var stores = new SelectList(
                await _StoreRepository.GetStoresByUserId(GetUserId()),
                nameof(StoreData.Id),
                nameof(StoreData.StoreName),
                new SearchString(model?.SearchTerm).GetFilterArray("storeid")?.ToArray().FirstOrDefault()
            );
            if (!stores.Any())
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to create at least one store before creating a transaction";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
            }

            return View(new CreateInvoiceModel() { Stores = stores, AvailablePaymentMethods = GetPaymentMethodsSelectList() });
        }

        [HttpPost]
        [Route("invoices/create")]
        [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model, CancellationToken cancellationToken)
        {
            var stores = await _StoreRepository.GetStoresByUserId(GetUserId());
            model.Stores = new SelectList(stores, nameof(StoreData.Id), nameof(StoreData.StoreName), model.StoreId);
            model.AvailablePaymentMethods = GetPaymentMethodsSelectList();
            var store = HttpContext.GetStoreData();
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!store.GetSupportedPaymentMethods(_NetworkProvider).Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"To create an invoice, you need to <a href='{Url.Action(nameof(StoresController.PaymentMethods), "Stores", new { storeId = store.Id })}' class='alert-link'>set up your wallet</a> first",
                    AllowDismiss = false
                });
                return View(model);
            }

            try
            {
                var result = await CreateInvoiceCore(new BitpayCreateInvoiceRequest()
                {
                    Price = model.Amount,
                    Currency = model.Currency,
                    PosData = model.PosData,
                    OrderId = model.OrderId,
                    //RedirectURL = redirect + "redirect",
                    NotificationURL = model.NotificationUrl,
                    ItemDesc = model.ItemDesc,
                    FullNotifications = true,
                    BuyerEmail = model.BuyerEmail,
                    SupportedTransactionCurrencies = model.SupportedTransactionCurrencies?.ToDictionary(s => s, s => new InvoiceSupportedTransactionCurrency()
                    {
                        Enabled = true
                    }),
                    DefaultPaymentMethod = model.DefaultPaymentMethod,
                    NotificationEmail = model.NotificationEmail,
                    ExtendedNotifications = model.NotificationEmail != null,
                    RequiresRefundEmail = model.RequiresRefundEmail == RequiresRefundEmail.InheritFromStore 
                        ? store.GetStoreBlob().RequiresRefundEmail
                        : model.RequiresRefundEmail == RequiresRefundEmail.On
                }, store, HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);

                TempData[WellKnownTempData.SuccessMessage] = $"Invoice {result.Data.Id} just created!";
                CreatedInvoiceId = result.Data.Id;
                return RedirectToAction(nameof(ListInvoices));
            }
            catch (BitpayHttpException ex)
            {
                Logs.PayServer.LogError(ex, $"Invoice creation failed due to invalid currency {model.Currency}");
                ModelState.TryAddModelError(nameof(model.Currency), "Please make sure you entered a valid currency symbol, a rate provider is configured in store settings, and your configured rate provider is both online and providing rates for your selected currency.");
                return View(model);
            }
        }

        [HttpPost]
        [Route("invoices/{invoiceId}/changestate/{newState}")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ChangeInvoiceState(string invoiceId, string newState)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId()
            })).FirstOrDefault();
            var model = new InvoiceStateChangeModel();
            if (invoice == null)
            {
                model.NotFound = true;
                return NotFound(model);
            }
            if (newState == "invalid")
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoiceId, InvoiceStatus.Invalid);
                model.StatusString = new InvoiceState("invalid", "marked").ToString();
            }
            else if (newState == "complete")
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoiceId, InvoiceStatus.Settled);
                model.StatusString = new InvoiceState("complete", "marked").ToString();
            }

            return Json(model);
        }

        public class InvoiceStateChangeModel
        {
            public bool NotFound { get; set; }
            public string? StatusString { get; set; }
        }

        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }

        public class PosDataParser
        {
            public static Dictionary<string, object> ParsePosData(string posData)
            {
                var result = new Dictionary<string, object>();
                if (string.IsNullOrEmpty(posData))
                {
                    return result;
                }

                try
                {
                    var jObject = JObject.Parse(posData);
                    foreach (var item in jObject)
                    {
                        switch (item.Value?.Type)
                        {
                            case JTokenType.Array:
                                var items = item.Value.AsEnumerable().ToList();
                                for (var i = 0; i < items.Count; i++)
                                {
                                    result.TryAdd($"{item.Key}[{i}]", ParsePosData(items[i].ToString()));
                                }
                                break;
                            case JTokenType.Object:
                                result.TryAdd(item.Key, ParsePosData(item.Value.ToString()));
                                break;
                            case null:
                                break;
                            default:
                                result.TryAdd(item.Key, item.Value.ToString());
                                break;
                        }

                    }
                }
                catch
                {
                    result.TryAdd(string.Empty, posData);
                }
                return result;
            }
        }

    }
}
