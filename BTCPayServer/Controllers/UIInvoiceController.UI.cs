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
using BTCPayServer.Models;
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
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitpayClient;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using BitpayCreateInvoiceRequest = BTCPayServer.Models.BitpayCreateInvoiceRequest;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    public partial class UIInvoiceController
    {
        [HttpGet("invoices/{invoiceId}/deliveries/{deliveryId}/request")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> WebhookDelivery(string invoiceId, string deliveryId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId()
            })).FirstOrDefault();
            if (invoice is null)
                return NotFound();
            var delivery = await _InvoiceRepository.GetWebhookDelivery(invoiceId, deliveryId);
            if (delivery is null)
                return NotFound();
            return File(delivery.GetBlob().Request, "application/json");
        }

        [HttpPost("invoices/{invoiceId}/deliveries/{deliveryId}/redeliver")]
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

        [HttpGet("invoices/{invoiceId}")]
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Invoice(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId(),
                IncludeAddresses = true,
                IncludeEvents = true,
                IncludeArchived = true,
                IncludeRefunds = true,
            })).FirstOrDefault();
            if (invoice == null)
                return NotFound();

            var store = await _StoreRepository.FindStore(invoice.StoreId);
            if (store == null)
                return NotFound();
            
            var invoiceState = invoice.GetInvoiceState();
            var model = new InvoiceDetailsModel
            {
                StoreId = store.Id,
                StoreName = store.StoreName,
                StoreLink = Url.Action(nameof(UIStoresController.GeneralSettings), "UIStores", new { storeId = store.Id }),
                PaymentRequestLink = Url.Action(nameof(UIPaymentRequestController.ViewPaymentRequest), "UIPaymentRequest", new { payReqId = invoice.Metadata.PaymentRequestId }),
                Id = invoice.Id,
                State = invoiceState,
                TransactionSpeed = invoice.SpeedPolicy == SpeedPolicy.HighSpeed ? "high" :
                                   invoice.SpeedPolicy == SpeedPolicy.MediumSpeed ? "medium" :
                                   invoice.SpeedPolicy == SpeedPolicy.LowMediumSpeed ? "low-medium" :
                                   "low",
                RefundEmail = invoice.RefundMail,
                CreatedDate = invoice.InvoiceTime,
                ExpirationDate = invoice.ExpirationTime,
                MonitoringDate = invoice.MonitoringExpiration,
                Fiat = _CurrencyNameTable.DisplayFormatCurrency(invoice.Price, invoice.Currency),
                TaxIncluded = invoice.Metadata.TaxIncluded is null
                    ? null
                    : _CurrencyNameTable.DisplayFormatCurrency(invoice.Metadata.TaxIncluded ?? 0.0m, invoice.Currency),
                NotificationUrl = invoice.NotificationURL?.AbsoluteUri,
                RedirectUrl = invoice.RedirectURL?.AbsoluteUri,
                TypedMetadata = invoice.Metadata,
                StatusException = invoice.ExceptionStatus,
                Events = invoice.Events,
                PosData = PosDataParser.ParsePosData(invoice.Metadata.PosData),
                Archived = invoice.Archived,
                CanRefund = CanRefund(invoiceState),
                Refunds = invoice.Refunds,
                ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
                Deliveries = (await _InvoiceRepository.GetWebhookDeliveries(invoiceId))
                                    .Select(c => new Models.StoreViewModels.DeliveryViewModel(c))
                                    .ToList(),
                CanMarkInvalid = invoiceState.CanMarkInvalid(),
                CanMarkSettled = invoiceState.CanMarkComplete(),
            };

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

        [HttpGet("invoices/{invoiceId}/refund")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund([FromServices] IEnumerable<IPayoutHandler> payoutHandlers, string invoiceId, CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var invoice = await ctx.Invoices.Include(i => i.Payments)
                                            .Include(i => i.CurrentRefund)
                                            .Include(i => i.StoreData)
                                            .ThenInclude(data => data.UserStores)
                                            .Include(i => i.CurrentRefund.PullPaymentData)
                                            .Where(i => i.Id == invoiceId)
                                            .FirstOrDefaultAsync(cancellationToken);
            if (invoice is null)
                return NotFound();
            if (invoice.CurrentRefund?.PullPaymentDataId is null && GetUserId() is null)
                return NotFound();
            if (!CanRefund(invoice.GetInvoiceState()))
                return NotFound();
            if (invoice.CurrentRefund?.PullPaymentDataId is string ppId && !invoice.CurrentRefund.PullPaymentData.Archived)
            {
                // TODO: Having dedicated UI later on
                return RedirectToAction(nameof(UIPullPaymentController.ViewPullPayment),
                                "UIPullPayment",
                                new { pullPaymentId = ppId });
            }

            var paymentMethods = invoice.GetBlob(_NetworkProvider).GetPaymentMethods();
            var pmis = paymentMethods.Select(method => method.GetId()).ToList();
            var options = (await payoutHandlers.GetSupportedPaymentMethods(invoice.StoreData)).Where(id => pmis.Contains(id)).ToList();
            if (!options.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "There were no payment methods available to provide refunds with for this invoice."
                });
                return RedirectToAction(nameof(Invoice), new { invoiceId });
            }

            var defaultRefund = invoice.Payments
                .Select(p => p.GetBlob(_NetworkProvider))
                .Select(p => p?.GetPaymentMethodId())
                .FirstOrDefault(p => p != null && options.Contains(p));
            // TODO: What if no option?
            var refund = new RefundModel
            {
                Title = "Payment method",
                AvailablePaymentMethods =
                    new SelectList(options.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString())),
                        "Value", "Text"),
                SelectedPaymentMethod = defaultRefund?.ToString() ?? options.First().ToString()
            };

            // Nothing to select, skip to next
            if (refund.AvailablePaymentMethods.Count() == 1)
            {
                return await Refund(invoiceId, refund, cancellationToken);
            }
            return View("_RefundModal", refund);
        }

        [HttpPost("invoices/{invoiceId}/refund")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund(string invoiceId, RefundModel model, CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();

            var invoice = GetCurrentInvoice();
            if (invoice == null)
                return NotFound();

            if (!CanRefund(invoice.GetInvoiceState()))
                return NotFound();

            var store = GetCurrentStore();
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
                    model.Title = "How much to refund?";
                    var pms = invoice.GetPaymentMethods();
                    var paymentMethod = pms.SingleOrDefault(method => method.GetId() == paymentMethodId);

                    //TODO: Make this clean
                    if (paymentMethod is null && paymentMethodId.PaymentType == LightningPaymentType.Instance)
                    {
                        paymentMethod = pms[new PaymentMethodId(paymentMethodId.CryptoCode, PaymentTypes.LNURLPay)];
                    }

                    if (paymentMethod != null)
                    {
                        var cryptoPaid = paymentMethod.Calculate().Paid.ToDecimal(MoneyUnit.BTC);
                        var paidCurrency = Math.Round(cryptoPaid * paymentMethod.Rate, cdCurrency.Divisibility);
                        model.CryptoAmountThen = cryptoPaid.RoundToSignificant(paymentMethodDivisibility);
                        model.RateThenText =
                            _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountThen, paymentMethodId.CryptoCode);
                        rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
                        rateResult = await _RateProvider.FetchRate(
                            new CurrencyPair(paymentMethodId.CryptoCode, invoice.Currency), rules,
                            cancellationToken);
                        //TODO: What if fetching rate failed?
                        if (rateResult.BidAsk is null)
                        {
                            ModelState.AddModelError(nameof(model.SelectedRefundOption),
                                $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                            return View("_RefundModal", model);
                        }

                        model.CryptoAmountNow = Math.Round(paidCurrency / rateResult.BidAsk.Bid, paymentMethodDivisibility);
                        model.CurrentRateText =
                            _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountNow, paymentMethodId.CryptoCode);
                        model.FiatAmount = paidCurrency;
                    }
                    model.CustomAmount = model.FiatAmount;
                    model.CustomCurrency = invoice.Currency;
                    model.FiatText = _CurrencyNameTable.DisplayFormatCurrency(model.FiatAmount, invoice.Currency);
                    return View("_RefundModal", model);

                case RefundSteps.SelectRate:
                    createPullPayment = new CreatePullPayment
                    {
                        Name = $"Refund {invoice.Id}",
                        PaymentMethodIds = new[] { paymentMethodId },
                        StoreId = invoice.StoreId,
                        BOLT11Expiration = store.GetStoreBlob().RefundBOLT11Expiration
                    };
                    switch (model.SelectedRefundOption)
                    {
                        case "RateThen":
                            createPullPayment.Currency = paymentMethodId.CryptoCode;
                            createPullPayment.Amount = model.CryptoAmountThen;
                            createPullPayment.AutoApproveClaims = true;
                            break;
                        
                        case "CurrentRate":
                            createPullPayment.Currency = paymentMethodId.CryptoCode;
                            createPullPayment.Amount = model.CryptoAmountNow;
                            createPullPayment.AutoApproveClaims = true;
                            break;
                        
                        case "Fiat":
                            createPullPayment.Currency = invoice.Currency;
                            createPullPayment.Amount = model.FiatAmount;
                            createPullPayment.AutoApproveClaims = false;
                            break;
                        
                        case "Custom":
                            model.Title = "How much to refund?";
                            
                            model.RefundStep = RefundSteps.SelectRate;
                            
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
                                return View("_RefundModal", model);
                            }

                            rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
                            rateResult = await _RateProvider.FetchRate(
                                new CurrencyPair(paymentMethodId.CryptoCode, model.CustomCurrency), rules,
                                cancellationToken);
                            
                            //TODO: What if fetching rate failed?
                            if (rateResult.BidAsk is null)
                            {
                                ModelState.AddModelError(nameof(model.SelectedRefundOption),
                                    $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                                return View("_RefundModal", model);
                            }

                            createPullPayment.Currency = model.CustomCurrency;
                            createPullPayment.Amount = model.CustomAmount;
                            createPullPayment.AutoApproveClaims = paymentMethodId.CryptoCode == model.CustomCurrency;
                            break;
                        
                        default:
                            ModelState.AddModelError(nameof(model.SelectedRefundOption), "Please select an option before proceeding");
                            return View("_RefundModal", model);
                    }
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var ppId = await _paymentHostedService.CreatePullPayment(createPullPayment);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Html = "Refund successfully created!<br />Share the link to this page with a customer.<br />The customer needs to enter their address and claim the refund.<br />Once a customer claims the refund, you will get a notification and would need to approve and initiate it from your Store > Payouts.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            (await ctx.Invoices.FindAsync(new[] { invoice.Id }, cancellationToken))!.CurrentRefundId = ppId;
            ctx.Refunds.Add(new RefundData
            {
                InvoiceDataId = invoice.Id,
                PullPaymentDataId = ppId
            });
            await ctx.SaveChangesAsync(cancellationToken);
            
            // TODO: Having dedicated UI later on
            return RedirectToAction(nameof(UIPullPaymentController.ViewPullPayment),
                "UIPullPayment",
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
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ToggleArchive(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId(),
                IncludeAddresses = false,
                IncludeEvents = false,
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
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        public async Task<IActionResult> MassAction(string command, string[] selectedItems, string? storeId = null)
        {
            if (selectedItems != null)
            {
                switch (command)
                {
                    case "archive":
                        await _InvoiceRepository.MassArchive(selectedItems);
                        TempData[WellKnownTempData.SuccessMessage] = $"{selectedItems.Length} invoice{(selectedItems.Length == 1 ? "" : "s")} archived.";
                        break;

                    case "unarchive":
                        await _InvoiceRepository.MassArchive(selectedItems, false);
                        TempData[WellKnownTempData.SuccessMessage] = $"{selectedItems.Length} invoice{(selectedItems.Length == 1 ? "" : "s")} unarchived.";
                        break;
                    case "cpfp":
                        if (selectedItems.Length == 0)
                            return NotSupported("No invoice has been selected");
                        var network = _NetworkProvider.DefaultNetwork;
                        var explorer = _ExplorerClients.GetExplorerClient(network);
                        IActionResult NotSupported(string err)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = err;
                            return RedirectToAction(nameof(ListInvoices), new { storeId });
                        }
                        if (explorer is null)
                            return NotSupported("This feature is only available to BTC wallets");
                        if (this.GetCurrentStore().Role != StoreRoles.Owner)
                            return Forbid();
                        
                        var settings = (this.GetCurrentStore().GetDerivationSchemeSettings(_NetworkProvider, network.CryptoCode));
                        var derivationScheme = settings.AccountDerivation;
                        if (derivationScheme is null)
                            return NotSupported("This feature is only available to BTC wallets");
                        var bumpableAddresses = (await GetAddresses(selectedItems))
                                                .Where(p => p.GetPaymentMethodId().IsBTCOnChain)
                                                .Select(p => p.GetAddress()).ToHashSet();
                        var utxos = await explorer.GetUTXOsAsync(derivationScheme);
                        var bumpableUTXOs = utxos.GetUnspentUTXOs().Where(u => u.Confirmations == 0 && bumpableAddresses.Contains(u.ScriptPubKey.Hash.ToString())).ToArray();
                        var parameters = new MultiValueDictionary<string, string>();
                        foreach (var utxo in bumpableUTXOs)
                        {
                            parameters.Add($"outpoints[]", utxo.Outpoint.ToString());
                        }
                        return View("PostRedirect", new PostRedirectViewModel
                        {
                            AspController = "UIWallets",
                            AspAction = nameof(UIWalletsController.WalletCPFP),
                            RouteParameters = {
                                { "walletId", new WalletId(storeId, network.CryptoCode).ToString() },
                                { "returnUrl", Url.Action(nameof(ListInvoices), new { storeId }) }
                            },
                            FormParameters = parameters,
                        });
                }
            }
            return RedirectToAction(nameof(ListInvoices), new { storeId });
        }

        private async Task<AddressInvoiceData[]> GetAddresses(string[] selectedItems)
        {
            using var ctx = _dbContextFactory.CreateContext();
            return await ctx.AddressInvoices.Where(i => selectedItems.Contains(i.InvoiceDataId)).ToArrayAsync();
        }

        [HttpGet("i/{invoiceId}")]
        [HttpGet("i/{invoiceId}/{paymentMethodId}")]
        [HttpGet("invoice")]
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

        [HttpGet("invoice-noscript")]
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
            if (store == null)
                return null;
            
            bool isDefaultPaymentId = false;
            if (paymentMethodId is null)
            {
                var enabledPaymentIds = store.GetEnabledPaymentIds(_NetworkProvider);
                PaymentMethodId? invoicePaymentId = invoice.GetDefaultPaymentMethod();
                PaymentMethodId? storePaymentId = store.GetDefaultPaymentId();
                if (invoicePaymentId is not null)
                {
                    if (enabledPaymentIds.Contains(invoicePaymentId))
                        paymentMethodId = invoicePaymentId;
                }
                if (paymentMethodId is null && storePaymentId is not null)
                {
                    if (enabledPaymentIds.Contains(storePaymentId))
                        paymentMethodId = storePaymentId;
                }
                if (paymentMethodId is null && invoicePaymentId is not null)
                {
                    paymentMethodId = invoicePaymentId.FindNearest(enabledPaymentIds);
                }
                if (paymentMethodId is null && storePaymentId is not null)
                {
                    paymentMethodId = storePaymentId.FindNearest(enabledPaymentIds);
                }
                if (paymentMethodId is null)
                {
                    paymentMethodId = enabledPaymentIds.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.BTCLike) ??
                                      enabledPaymentIds.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.LightningLike) ??
                                      enabledPaymentIds.FirstOrDefault();
                }
                isDefaultPaymentId = true;
            }
            if (paymentMethodId is null)
                return null;
            BTCPayNetworkBase network = _NetworkProvider.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
            if (network is null || !invoice.Support(paymentMethodId))
            {
                if (!isDefaultPaymentId)
                    return null;
                var paymentMethodTemp = invoice
                    .GetPaymentMethods()
                    .FirstOrDefault(c => paymentMethodId.CryptoCode == c.GetId().CryptoCode);
                if (paymentMethodTemp == null)
                    paymentMethodTemp = invoice.GetPaymentMethods().FirstOrDefault();
                if (paymentMethodTemp is null)
                    return null;
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

            var model = new PaymentModel
            {
                Activated = paymentMethodDetails.Activated,
                CryptoCode = network.CryptoCode,
                RootPath = Request.PathBase.Value.WithTrailingSlash(),
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
                                                          invoiceId,
                                                          paymentMethodId = kv.GetId().ToString()
                                                      })
                                              };
                                          }).Where(c => c.CryptoImage != "/")
                                          .OrderByDescending(a => a.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode).ThenBy(a => a.PaymentMethodName).ThenBy(a => a.IsLightning ? 1 : 0)
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

        [HttpGet("i/{invoiceId}/status")]
        [HttpGet("i/{invoiceId}/{implicitPaymentMethodId}/status")]
        [HttpGet("invoice/{invoiceId}/status")]
        [HttpGet("invoice/{invoiceId}/{implicitPaymentMethodId}/status")]
        [HttpGet("invoice/status")]
        public async Task<IActionResult> GetStatus(string invoiceId, string? paymentMethodId = null, string? implicitPaymentMethodId = null, [FromQuery] string? lang = null)
        {
            if (string.IsNullOrEmpty(paymentMethodId))
                paymentMethodId = implicitPaymentMethodId;
            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId), lang);
            if (model == null)
                return NotFound();
            return Json(model);
        }

        [HttpGet("i/{invoiceId}/status/ws")]
        [HttpGet("i/{invoiceId}/{paymentMethodId}/status/ws")]
        [HttpGet("invoice/{invoiceId}/status/ws")]
        [HttpGet("invoice/{invoiceId}/{paymentMethodId}/status")]
        [HttpGet("invoice/status/ws")]
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
                    var message = await webSocket.ReceiveAndPingAsync(DummyBuffer);
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

        [HttpPost("i/{invoiceId}/UpdateCustomer")]
        [HttpPost("invoice/UpdateCustomer")]
        public async Task<IActionResult> UpdateCustomer(string invoiceId, [FromBody] UpdateCustomerModel data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            await _InvoiceRepository.UpdateInvoice(invoiceId, data).ConfigureAwait(false);
            return Ok("{}");
        }

        [HttpGet("/stores/{storeId}/invoices")]
        [HttpGet("invoices")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ListInvoices(InvoicesModel? model = null)
        {
            model = this.ParseListQuery(model ?? new InvoicesModel());
            var fs = new SearchString(model.SearchTerm);
            string? storeId = model.StoreId;
            var storeIds = new HashSet<string>();
            if (fs.GetFilterArray("storeid") is string[] l)
            {
                foreach (var i in l)
                    storeIds.Add(i);
            }
            if (storeId is not null)
            {
                storeIds.Add(storeId);
                model.StoreId = storeId;
            }
            model.StoreIds = storeIds.ToArray();

            InvoiceQuery invoiceQuery = GetInvoiceQuery(model.SearchTerm, model.TimezoneOffset ?? 0);
            invoiceQuery.StoreId = model.StoreIds;
            invoiceQuery.Take = model.Count;
            invoiceQuery.Skip = model.Skip;
            var list = await _InvoiceRepository.GetInvoices(invoiceQuery);

            model.IncludeArchived = invoiceQuery.IncludeArchived;

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
                    CanMarkSettled = state.CanMarkComplete(),
                    Details = InvoicePopulatePayments(invoice),
                });
            }
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
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> Export(string format, string? searchTerm = null, int timezoneOffset = 0)
        {
            var model = new InvoiceExport(_CurrencyNameTable);

            InvoiceQuery invoiceQuery = GetInvoiceQuery(searchTerm, timezoneOffset);
            invoiceQuery.StoreId = new[] { GetCurrentStore().Id };
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
            var store = GetCurrentStore();
            var excludeFilter = store.GetStoreBlob().GetExcludedPaymentMethods();

            return new SelectList(store.GetSupportedPaymentMethods(_NetworkProvider)
                        .Where(s => !excludeFilter.Match(s.PaymentId))
                        .Select(method => new SelectListItem(method.PaymentId.ToPrettyString(), method.PaymentId.ToString())),
                nameof(SelectListItem.Value),
                nameof(SelectListItem.Text));
        }

        private bool AnyPaymentMethodAvailable(StoreData store)
        {
            var storeBlob = store.GetStoreBlob();
            var excludeFilter = storeBlob.GetExcludedPaymentMethods();
            
            return store.GetSupportedPaymentMethods(_NetworkProvider).Where(s => !excludeFilter.Match(s.PaymentId)).Any();
        }

        [HttpGet("/stores/{storeId}/invoices/create")]
        [HttpGet("invoices/create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(InvoicesModel? model = null)
        {
            if (model?.StoreId != null)
            {
                var store = await _StoreRepository.FindStore(model.StoreId, GetUserId());
                if (store == null)
                    return NotFound();

                if (!AnyPaymentMethodAvailable(store))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Html = $"To create an invoice, you need to <a href='{Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { cryptoCode = _NetworkProvider.DefaultNetwork.CryptoCode, storeId = store.Id })}' class='alert-link'>set up a wallet</a> first",
                        AllowDismiss = false
                    });
                }

                HttpContext.SetStoreData(store);
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to select a store before creating an invoice.";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }

            var vm = new CreateInvoiceModel
            {
                StoreId = model.StoreId,
                Currency = HttpContext.GetStoreData()?.GetStoreBlob().DefaultCurrency,
                AvailablePaymentMethods = GetPaymentMethodsSelectList()
            };

            return View(vm);
        }

        [HttpPost("/stores/{storeId}/invoices/create")]
        [HttpPost("invoices/create")]
        [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model, CancellationToken cancellationToken)
        {
            model.AvailablePaymentMethods = GetPaymentMethodsSelectList();
            var store = HttpContext.GetStoreData();
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!AnyPaymentMethodAvailable(store))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"To create an invoice, you need to <a href='{Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { cryptoCode = _NetworkProvider.DefaultNetwork.CryptoCode, storeId = store.Id })}' class='alert-link'>set up a wallet</a> first",
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
                return RedirectToAction(nameof(ListInvoices), new { result.Data.StoreId });
            }
            catch (BitpayHttpException ex)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = ex.Message
                });
                return View(model);
            }
        }

        [HttpPost]
        [Route("invoices/{invoiceId}/changestate/{newState}")]
        [Route("stores/{storeId}/invoices/{invoiceId}/changestate/{newState}")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ChangeInvoiceState(string invoiceId, string newState)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery
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
                model.StatusString = new InvoiceState(InvoiceStatusLegacy.Invalid, InvoiceExceptionStatus.Marked).ToString();
            }
            else if (newState == "settled")
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoiceId, InvoiceStatus.Settled);
                model.StatusString = new InvoiceState(InvoiceStatusLegacy.Complete, InvoiceExceptionStatus.Marked).ToString();
            }

            return Json(model);
        }

        public class InvoiceStateChangeModel
        {
            public bool NotFound { get; set; }
            public string? StatusString { get; set; }
        }

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private InvoiceEntity GetCurrentInvoice() => HttpContext.GetInvoiceData();

        private string GetUserId() => _UserManager.GetUserId(User);

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
