#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    public partial class UIInvoiceController
    {
        static UIInvoiceController()
        {
            InvoiceAdditionalDataExclude =
                typeof(InvoiceMetadata)
                .GetProperties()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            InvoiceAdditionalDataExclude.Remove(nameof(InvoiceMetadata.PosData));
        }
        static readonly HashSet<string> InvoiceAdditionalDataExclude;

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
        [HttpGet("/stores/{storeId}/invoices/${invoiceId}")]
        [Authorize(Policy = Policies.CanViewInvoices, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Invoice(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery
            {
                InvoiceId = new[] { invoiceId },
                UserId = GetUserId(),
                IncludeAddresses = true,
                IncludeArchived = true,
                IncludeRefunds = true,
            })).FirstOrDefault();
            if (invoice == null)
                return NotFound();

            var store = await _StoreRepository.FindStore(invoice.StoreId);
            if (store == null)
                return NotFound();

            var receipt = InvoiceDataBase.ReceiptOptions.Merge(store.GetStoreBlob().ReceiptOptions, invoice.ReceiptOptions);
            var invoiceState = invoice.GetInvoiceState();
            var metaData = PosDataParser.ParsePosData(invoice.Metadata.ToJObject());
            var additionalData = metaData
                .Where(dict => !InvoiceAdditionalDataExclude.Contains(dict.Key))
                .ToDictionary(dict => dict.Key, dict => dict.Value);

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
                CreatedDate = invoice.InvoiceTime,
                ExpirationDate = invoice.ExpirationTime,
                MonitoringDate = invoice.MonitoringExpiration,
                Fiat = _displayFormatter.Currency(invoice.Price, invoice.Currency),
                TaxIncluded = invoice.Metadata.TaxIncluded is null
                    ? null
                    : _displayFormatter.Currency(invoice.Metadata.TaxIncluded ?? 0.0m, invoice.Currency),
                NotificationUrl = invoice.NotificationURL?.AbsoluteUri,
                RedirectUrl = invoice.RedirectURL?.AbsoluteUri,
                TypedMetadata = invoice.Metadata,
                StatusException = invoice.ExceptionStatus,
                Events = await _InvoiceRepository.GetInvoiceLogs(invoice.Id),
                Metadata = metaData,
                Archived = invoice.Archived,
                HasRefund = invoice.Refunds.Any(),
                CanRefund = invoiceState.CanRefund(),
                Refunds = invoice.Refunds,
                ShowCheckout = invoice.Status == InvoiceStatus.New,
                ShowReceipt = invoice.Status == InvoiceStatus.Settled && (invoice.ReceiptOptions?.Enabled ?? receipt.Enabled is true),
                Deliveries = (await _InvoiceRepository.GetWebhookDeliveries(invoiceId))
                                    .Select(c => new Models.StoreViewModels.DeliveryViewModel(c))
                                    .ToList()
            };

            var details = InvoicePopulatePayments(invoice);
            model.CryptoPayments = details.CryptoPayments;
            model.Payments = details.Payments;
            model.Overpaid = details.Overpaid;
            model.StillDue = details.StillDue;
            model.HasRates = details.HasRates;

            if (additionalData.TryGetValue("receiptData", out object? receiptData))
            {
                model.ReceiptData = (Dictionary<string, object>)receiptData;
                additionalData.Remove("receiptData");
            }

            if (additionalData.ContainsKey("posData") && additionalData["posData"] is string posData)
            {
                // overwrite with parsed JSON if possible
                try
                {
                    additionalData["posData"] = PosDataParser.ParsePosData(JObject.Parse(posData));
                }
                catch (Exception)
                {
                    additionalData["posData"] = posData;
                }
            }

            model.AdditionalData = additionalData;

            return View(model);
        }

        [HttpGet("i/{invoiceId}/receipt")]
        public async Task<IActionResult> InvoiceReceipt(string invoiceId, [FromQuery] bool print = false)
        {
            var i = await _InvoiceRepository.GetInvoice(invoiceId);
            if (i is null)
                return NotFound();
            var store = await _StoreRepository.GetStoreByInvoiceId(i.Id);
            if (store is null)
                return NotFound();

            var receipt = InvoiceDataBase.ReceiptOptions.Merge(store.GetStoreBlob().ReceiptOptions, i.ReceiptOptions);
            if (receipt.Enabled is not true)
            {
                return i.RedirectURL is not null
                    ? Redirect(i.RedirectURL.ToString())
                    : NotFound();
            }

            var storeBlob = store.GetStoreBlob();
            var vm = new InvoiceReceiptViewModel
            {
                InvoiceId = i.Id,
                OrderId = i.Metadata?.OrderId,
                RedirectUrl = i.RedirectURL?.AbsoluteUri ?? i.Metadata?.OrderUrl,
                Status = i.Status,
                Currency = i.Currency,
                Timestamp = i.InvoiceTime,
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob),
                ReceiptOptions = receipt
            };

            if (i.Status != InvoiceStatus.Settled)
            {
                return View(vm);
            }
            
            var metaData = PosDataParser.ParsePosData(i.Metadata?.ToJObject());
            var additionalData = metaData
                .Where(dict => !InvoiceAdditionalDataExclude.Contains(dict.Key))
                .ToDictionary(dict => dict.Key, dict => dict.Value);
                
            // Split receipt data into cart and additional data 
            if (additionalData.TryGetValue("receiptData", out object? combinedReceiptData))
            {
                var receiptData = new Dictionary<string, object>((Dictionary<string, object>)combinedReceiptData, StringComparer.OrdinalIgnoreCase);
                string[] cartKeys = ["cart", "subtotal", "discount", "tip", "total"];
                // extract cart data and lowercase keys to handle data uniformly in PosData partial
                if (receiptData.Keys.Any(key => cartKeys.Contains(key.ToLowerInvariant())))
                {
                    vm.CartData = new Dictionary<string, object>();
                    foreach (var key in cartKeys)
                    {
                        if (!receiptData.ContainsKey(key)) continue;
                        // add it to cart data and remove it from the general data
                        vm.CartData.Add(key.ToLowerInvariant(), receiptData[key]);
                        receiptData.Remove(key);
                    }
                }
                // assign the rest to additional data and remove empty values 
                if (receiptData.Any())
                {
                    vm.AdditionalData = receiptData
                        .Where(x => !string.IsNullOrEmpty(x.Value.ToString()))
                        .ToDictionary(x => x.Key, x => x.Value);
                }
            }

            var payments = ViewPaymentRequestViewModel.PaymentRequestInvoicePayment.GetViewModels(i, _displayFormatter, _transactionLinkProviders, _handlers);
            vm.Amount = i.PaidAmount.Net;
            vm.Payments = receipt.ShowPayments is false ? null : payments;

            return View(print ? "InvoiceReceiptPrint" : "InvoiceReceipt", vm);
        }

        [HttpGet("invoices/{invoiceId}/refund")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund(string invoiceId, CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var invoice = await ctx.Invoices.Include(i => i.Payments)
                                            .Include(i => i.Refunds).ThenInclude(i => i.PullPaymentData)
                                            .Include(i => i.StoreData)
                                            .ThenInclude(data => data.UserStores)
                                            .Where(i => i.Id == invoiceId)
                                            .FirstOrDefaultAsync(cancellationToken);
            if (invoice is null)
                return NotFound();
            var currentRefund = invoice.Refunds.OrderByDescending(r => r.PullPaymentData.StartDate).FirstOrDefault();
            if (currentRefund?.PullPaymentDataId is null && GetUserId() is null)
                return NotFound();
            if (!invoice.GetInvoiceState().CanRefund())
                return NotFound();
            if (currentRefund?.PullPaymentDataId is string ppId && !currentRefund.PullPaymentData.Archived)
            {
                // TODO: Having dedicated UI later on
                return RedirectToAction(nameof(UIPullPaymentController.ViewPullPayment),
                                "UIPullPayment",
                                new { pullPaymentId = ppId });
            }

            var payoutMethodIds = _payoutHandlers.GetSupportedPayoutMethods(this.GetCurrentStore());
            if (!payoutMethodIds.Any())
            {
                var vm = new RefundModel { Title = "No matching payment method" };
                ModelState.AddModelError(nameof(vm.AvailablePaymentMethods),
                    "There are no payment methods available to provide refunds with for this invoice.");
                return View("_RefundModal", vm);
            }

            // Find the most similar payment method to the one used for the invoice
            var defaultRefund =
                invoice.GetClosestPayoutMethodId(payoutMethodIds);

            var refund = new RefundModel
            {
                Title = "Payment method",
                AvailablePaymentMethods =
                    new SelectList(payoutMethodIds.Select(id => new SelectListItem(id.ToString(), id.ToString())),
                        "Value", "Text"),
                SelectedPayoutMethod = defaultRefund?.ToString() ?? payoutMethodIds.First().ToString()
            };

            // Nothing to select, skip to next
            if (refund.AvailablePaymentMethods.Count() == 1)
            {
                return await Refund(invoiceId, refund, cancellationToken);
            }
            return View("_RefundModal", refund);
        }

        [HttpPost("invoices/{invoiceId}/refund")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund(string invoiceId, RefundModel model, CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();

            var invoice = GetCurrentInvoice();
            if (invoice == null)
                return NotFound();

            if (!invoice.GetInvoiceState().CanRefund())
                return NotFound();

            var store = GetCurrentStore();
            var pmi = PayoutMethodId.Parse(model.SelectedPayoutMethod);
            var cdCurrency = _CurrencyNameTable.GetCurrencyData(invoice.Currency, true);
            RateRules rules;
            RateResult rateResult;
            CreatePullPayment createPullPayment;

            var pmis = _payoutHandlers.GetSupportedPayoutMethods(store);
            if (!pmis.Contains(pmi))
            {
                ModelState.AddModelError(nameof(model.SelectedPayoutMethod), $"Invalid payout method");
                return View("_RefundModal", model);
            }

            var paymentMethodId = invoice.GetClosestPaymentMethodId([pmi]);

            var paymentMethod = paymentMethodId is null ? null : invoice.GetPaymentPrompt(paymentMethodId);
            if (paymentMethod?.Currency is null)
            {
                ModelState.AddModelError(nameof(model.SelectedPayoutMethod), $"Invalid payout method");
                return View("_RefundModal", model);
            }

            var accounting = paymentMethod.Calculate();
            var cryptoPaid = accounting.Paid;
            var dueAmount = accounting.TotalDue;

            // If no payment, but settled and marked, assume it has been fully paid
            if (cryptoPaid is 0 && invoice is { Status: InvoiceStatus.Settled, ExceptionStatus: InvoiceExceptionStatus.Marked })
            {
                cryptoPaid = accounting.TotalDue;
                dueAmount = 0;
            }

            var paymentMethodCurrency = paymentMethod.Currency;

            var isPaidOver = invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver;
            decimal? overpaidAmount = isPaidOver ? Math.Round(cryptoPaid - dueAmount, paymentMethod.Divisibility) : null;
            int ppDivisibility = paymentMethod.Divisibility;
            switch (model.RefundStep)
            {
                case RefundSteps.SelectPaymentMethod:
                    model.RefundStep = RefundSteps.SelectRate;
                    model.Title = "How much to refund?";

                    var paidCurrency = Math.Round(cryptoPaid * paymentMethod.Rate, cdCurrency.Divisibility);
                    model.CryptoAmountThen = cryptoPaid.RoundToSignificant(paymentMethod.Divisibility);
                    model.RateThenText = _displayFormatter.Currency(model.CryptoAmountThen, paymentMethodCurrency);
                    rules = store.GetStoreBlob().GetRateRules(_defaultRules);
                    rateResult = await _RateProvider.FetchRate(
                        new CurrencyPair(paymentMethodCurrency, invoice.Currency), rules, new StoreIdRateContext(store.Id),
                        cancellationToken);
                    //TODO: What if fetching rate failed?
                    if (rateResult.BidAsk is null)
                    {
                        ModelState.AddModelError(nameof(model.SelectedRefundOption),
                            $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                        return View("_RefundModal", model);
                    }

                    model.CryptoAmountNow = Math.Round(paidCurrency / rateResult.BidAsk.Bid, paymentMethod.Divisibility);
                    model.CurrentRateText = _displayFormatter.Currency(model.CryptoAmountNow, paymentMethodCurrency);
                    model.FiatAmount = paidCurrency;

                    model.CryptoCode = paymentMethodCurrency;
                    model.CryptoDivisibility = paymentMethod.Divisibility;
                    model.InvoiceDivisibility = cdCurrency.Divisibility;
                    model.InvoiceCurrency = invoice.Currency;
                    model.CustomAmount = model.FiatAmount;
                    model.CustomCurrency = invoice.Currency;
                    model.SubtractPercentage = 0;
                    model.OverpaidAmount = overpaidAmount;
                    model.OverpaidAmountText = overpaidAmount != null ? _displayFormatter.Currency(overpaidAmount.Value, paymentMethodCurrency) : null;
                    model.FiatText = _displayFormatter.Currency(model.FiatAmount, invoice.Currency);
                    return View("_RefundModal", model);

                case RefundSteps.SelectRate:
                    createPullPayment = new CreatePullPayment
                    {
                        Name = $"Refund {invoice.Id}",
                        PayoutMethodIds = new[] { pmi },
                        StoreId = invoice.StoreId,
                        BOLT11Expiration = store.GetStoreBlob().RefundBOLT11Expiration
                    };
                    var authorizedForAutoApprove = (await
                            _authorizationService.AuthorizeAsync(User, invoice.StoreId, Policies.CanCreatePullPayments))
                        .Succeeded;
                    if (model.SubtractPercentage is < 0 or > 100)
                    {
                        ModelState.AddModelError(nameof(model.SubtractPercentage), "Percentage must be a numeric value between 0 and 100");
                    }
                    if (!ModelState.IsValid)
                    {
                        return View("_RefundModal", model);
                    }

                    switch (model.SelectedRefundOption)
                    {
                        case "RateThen":
                            createPullPayment.Currency = paymentMethodCurrency;
                            createPullPayment.Amount = model.CryptoAmountThen;
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove;
                            break;

                        case "CurrentRate":
                            createPullPayment.Currency = paymentMethodCurrency;
                            createPullPayment.Amount = model.CryptoAmountNow;
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove;
                            break;

                        case "Fiat":
                            ppDivisibility = cdCurrency.Divisibility;
                            createPullPayment.Currency = invoice.Currency;
                            createPullPayment.Amount = model.FiatAmount;
                            createPullPayment.AutoApproveClaims = false;
                            break;

                        case "OverpaidAmount":
                            model.Title = "How much to refund?";
                            model.RefundStep = RefundSteps.SelectRate;

                            if (!isPaidOver)
                            {
                                ModelState.AddModelError(nameof(model.SelectedRefundOption), "Invoice is not overpaid");
                            }
                            if (overpaidAmount == null)
                            {
                                ModelState.AddModelError(nameof(model.SelectedRefundOption), "Overpaid amount cannot be calculated");
                            }
                            if (!ModelState.IsValid)
                            {
                                return View("_RefundModal", model);
                            }

                            createPullPayment.Currency = paymentMethodCurrency;
                            createPullPayment.Amount = overpaidAmount!.Value;
                            createPullPayment.AutoApproveClaims = true;
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

                            rules = store.GetStoreBlob().GetRateRules(_defaultRules);
                            rateResult = await _RateProvider.FetchRate(
                                new CurrencyPair(paymentMethodCurrency, model.CustomCurrency), rules, new StoreIdRateContext(store.Id),
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
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove && paymentMethodCurrency == model.CustomCurrency;
                            break;

                        default:
                            ModelState.AddModelError(nameof(model.SelectedRefundOption), "Please select an option before proceeding");
                            return View("_RefundModal", model);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            // reduce by percentage
            if (model.SubtractPercentage is > 0 and <= 100)
            {
                var reduceByAmount = createPullPayment.Amount * (model.SubtractPercentage / 100);
                createPullPayment.Amount = Math.Round(createPullPayment.Amount - reduceByAmount, ppDivisibility);
            }

            var ppId = await _paymentHostedService.CreatePullPayment(createPullPayment);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Html = "Refund successfully created!<br />Share the link to this page with a customer.<br />The customer needs to enter their address and claim the refund.<br />Once a customer claims the refund, you will get a notification and would need to approve and initiate it from your Store > Payouts.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

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
            var overpaid = false;
            var stillDue = false;
            var hasRates = false;
            var model = new InvoiceDetailsModel
            {
                Archived = invoice.Archived,
                Payments = invoice.GetPayments(false),
                CryptoPayments = invoice.GetPaymentPrompts().Select(
                    data =>
                    {
                        var accounting = data.Calculate();
                        var paymentMethodId = data.PaymentMethodId;
                        var hasPayment = accounting.PaymentMethodPaid > 0;
                        var overpaidAmount = accounting.OverpaidHelper;
                        var rate = ExchangeRate(data.Currency, data);

                        if (rate is not null)
                            hasRates = true;
                        if (hasPayment && overpaidAmount > 0)
                            overpaid = true;
                        if (hasPayment && accounting.Due > 0)
                            stillDue = true;

                        return new InvoiceDetailsModel.CryptoPayment
                        {
                            Rate = rate,
                            PaymentMethodRaw = data,
                            PaymentMethodId = paymentMethodId,
                            PaymentMethod = paymentMethodId.ToString(),
                            TotalDue = _displayFormatter.Currency(accounting.TotalDue, data.Currency, divisibility: data.Divisibility),
                            Due = hasPayment ? _displayFormatter.Currency(accounting.Due, data.Currency, divisibility: data.Divisibility) : null,
                            Paid = hasPayment ? _displayFormatter.Currency(accounting.PaymentMethodPaid, data.Currency, divisibility: data.Divisibility) : null,
                            Overpaid = hasPayment ? _displayFormatter.Currency(overpaidAmount, data.Currency, divisibility: data.Divisibility) : null,
                            Address = data.Destination
                        };
                    }).ToList(),
                Overpaid = overpaid,
                StillDue = stillDue,
                HasRates = hasRates
            };

            return model;
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
            IActionResult NotSupported(string err)
            {
                TempData[WellKnownTempData.ErrorMessage] = err;
                return RedirectToAction(nameof(ListInvoices), new { storeId });
            }
            if (selectedItems.Length == 0)
                return NotSupported("No invoice has been selected");

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
                case "cpfp" when storeId is not null:
                    var network = _NetworkProvider.DefaultNetwork;
                    var explorer = _ExplorerClients.GetExplorerClient(network);
                    if (explorer is null)
                        return NotSupported("This feature is only available to BTC wallets");
                    if (!GetCurrentStore().HasPermission(GetUserId(), Policies.CanModifyStoreSettings))
                        return Forbid();

                    var derivationScheme = (this.GetCurrentStore().GetDerivationSchemeSettings(_handlers, network.CryptoCode))?.AccountDerivation;
                    if (derivationScheme is null)
                        return NotSupported("This feature is only available to BTC wallets");
                    var btc = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
                    var bumpableAddresses = await GetAddresses(btc, selectedItems);
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
            return RedirectToAction(nameof(ListInvoices), new { storeId });
        }

        private async Task<HashSet<string>> GetAddresses(PaymentMethodId paymentMethodId, string[] selectedItems)
        {
            using var ctx = _dbContextFactory.CreateContext();
            return new HashSet<string>(await ctx.AddressInvoices.Where(i => selectedItems.Contains(i.InvoiceDataId) && i.PaymentMethodId == paymentMethodId.ToString()).Select(i => i.Address).ToArrayAsync());
        }

        [HttpGet("i/{invoiceId}")]
        [HttpGet("i/{invoiceId}/{paymentMethodId}")]
        [HttpGet("invoice")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest", false)]
        [XFrameOptions(null)]
        [ReferrerPolicy("origin")]
        public async Task<IActionResult> Checkout(string? invoiceId, string? id = null, string? paymentMethodId = null,
            [FromQuery] string? view = null, [FromQuery] string? lang = null)
        {
            // Keep compatibility with Bitpay
            invoiceId ??= id;

            if (invoiceId is null)
                return NotFound();

            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId), lang);
            if (model == null)
            {
                // see if the invoice actually exists and is in a state for which we do not display the checkout
                var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
                var store = invoice != null ? await _StoreRepository.GetStoreByInvoiceId(invoice.Id) : null;
                var receipt = invoice != null && store != null ? InvoiceDataBase.ReceiptOptions.Merge(store.GetStoreBlob().ReceiptOptions, invoice.ReceiptOptions) : null;
                var redirectUrl = invoice?.RedirectURL?.ToString();
                return receipt?.Enabled is true
                    ? RedirectToAction(nameof(InvoiceReceipt), new { invoiceId })
                    : !string.IsNullOrEmpty(redirectUrl) ? Redirect(redirectUrl) : NotFound();
            }

            if (view == "modal")
                model.IsModal = true;

            return View(model);
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
            var storeBlob = store.GetStoreBlob();

            var displayedPaymentMethods = invoice.GetPaymentPrompts().Select(p => p.PaymentMethodId).ToList();

            
            var btcId = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
            var lnurlId = PaymentTypes.LNURL.GetPaymentMethodId("BTC");
            var lnId = PaymentTypes.LN.GetPaymentMethodId("BTC");

            // Exclude Lightning if OnChainWithLnInvoiceFallback is active and we have both payment methods
            if (storeBlob is { OnChainWithLnInvoiceFallback: true } &&
                displayedPaymentMethods.Contains(btcId))
            {
                displayedPaymentMethods.Remove(lnId);
                displayedPaymentMethods.Remove(lnurlId);
            }

            // BOLT11 doesn't really support payment without amount
            if (invoice.IsUnsetTopUp())
                displayedPaymentMethods.Remove(lnId);

            // Exclude lnurl if bolt11 is available
            if (displayedPaymentMethods.Contains(lnId) && displayedPaymentMethods.Contains(lnurlId))
                displayedPaymentMethods.Remove(lnurlId);


            if (paymentMethodId is not null && !displayedPaymentMethods.Contains(paymentMethodId))
                paymentMethodId = null;
            if (paymentMethodId is null)
            {
                PaymentMethodId? invoicePaymentId = invoice.DefaultPaymentMethod;
                PaymentMethodId? storePaymentId = store.GetDefaultPaymentId();
                if (invoicePaymentId is not null)
                {
                    if (displayedPaymentMethods.Contains(invoicePaymentId))
                        paymentMethodId = invoicePaymentId;
                }
                if (paymentMethodId is null && storePaymentId is not null)
                {
                    if (displayedPaymentMethods.Contains(storePaymentId))
                        paymentMethodId = storePaymentId;
                }
                if (paymentMethodId is null && invoicePaymentId is not null)
                {
                    paymentMethodId = invoicePaymentId.FindNearest(displayedPaymentMethods);
                }
                if (paymentMethodId is null && storePaymentId is not null)
                {
                    paymentMethodId = storePaymentId.FindNearest(displayedPaymentMethods);
                }
                if (paymentMethodId is null)
                {
                    var defaultBTC = PaymentTypes.CHAIN.GetPaymentMethodId(_NetworkProvider.DefaultNetwork.CryptoCode);
                    var defaultLNURLPay  = PaymentTypes.LNURL.GetPaymentMethodId(_NetworkProvider.DefaultNetwork.CryptoCode);
                    paymentMethodId = displayedPaymentMethods.FirstOrDefault(e => e == defaultBTC) ??
                                      displayedPaymentMethods.FirstOrDefault(e => e == defaultLNURLPay) ??
                                      displayedPaymentMethods.FirstOrDefault();
                }
                isDefaultPaymentId = true;
            }
            if (paymentMethodId is null)
                return null;
            if (!invoice.Support(paymentMethodId))
            {
                if (!isDefaultPaymentId)
                    return null;
                var paymentMethodTemp = invoice
                    .GetPaymentPrompts()
                    .Where(p => displayedPaymentMethods.Contains(p.PaymentMethodId))
                    .FirstOrDefault();
                if (paymentMethodTemp is null)
                    return null;
                paymentMethodId = paymentMethodTemp.PaymentMethodId;
            }
            if (!_handlers.TryGetValue(paymentMethodId, out var handler))
                return null;

            // We activate the default payment method, and also those which aren't displayed (as they can't be set as default)
            bool activated = false;
            PaymentPrompt? prompt = null;
            foreach (var pm in invoice.GetPaymentPrompts())
            {
                var pmi = pm.PaymentMethodId;
                if (pmi == paymentMethodId)
                    prompt = pm;
                if (pmi != paymentMethodId || !displayedPaymentMethods.Contains(pmi))
                    continue;
                if (!pm.Activated)
                {
                    if (await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, pmi))
                    {
                        activated = true;
                    }
                }
            }
            if (prompt is null)
                return null;
            if (activated)
                return await GetInvoiceModel(invoiceId, paymentMethodId, lang);

            var accounting = prompt.Calculate();

            switch (lang?.ToLowerInvariant())
            {
                case "auto":
                case null when storeBlob.AutoDetectLanguage:
                    lang = _languageService.AutoDetectLanguageUsingHeader(HttpContext.Request.Headers, null)?.Code;
                    break;
                case { } langs when !string.IsNullOrEmpty(langs):
                    {
                        lang = _languageService.FindLanguage(langs)?.Code;
                        break;
                    }
            }
            lang ??= storeBlob.DefaultLang;

            var receiptEnabled = InvoiceDataBase.ReceiptOptions.Merge(storeBlob.ReceiptOptions, invoice.ReceiptOptions).Enabled is true;
            var receiptUrl = receiptEnabled ? _linkGenerator.GetUriByAction(
                nameof(InvoiceReceipt),
                "UIInvoice",
                new { invoiceId },
                Request.Scheme,
                Request.Host,
                Request.PathBase) : null;

            var orderId = invoice.Metadata.OrderId;
            var supportUrl = !string.IsNullOrEmpty(storeBlob.StoreSupportUrl)
                ? storeBlob.StoreSupportUrl
                    .Replace("{OrderId}", string.IsNullOrEmpty(orderId) ? string.Empty : Uri.EscapeDataString(orderId))
                    .Replace("{InvoiceId}", Uri.EscapeDataString(invoice.Id))
                : null;

            string GetPaymentMethodImage(PaymentMethodId paymentMethodId)
            {
                _paymentModelExtensions.TryGetValue(paymentMethodId, out var extension);
                return extension?.Image ?? "";
            }

			// Show the "Common divisibility" rather than the payment method disibility.
			// For example, BTC has commonly 8 digits, but on lightning it has 11. In this case, pick 8.
			if (this._CurrencyNameTable.GetCurrencyData(prompt.Currency, false)?.Divisibility is not int divisibility)
				divisibility = prompt.Divisibility;

			string ShowMoney(decimal value) => MoneyExtensions.ShowMoney(value, divisibility);
            var model = new PaymentModel
            {
                Activated = prompt.Activated,
                PaymentMethodName = _prettyName.PrettyName(paymentMethodId),
                CryptoCode = prompt.Currency,
                RootPath = Request.PathBase.Value.WithTrailingSlash(),
                OrderId = orderId,
                InvoiceId = invoiceId,
                DefaultLang = lang ?? invoice.DefaultLanguage ?? storeBlob.DefaultLang ?? "en",
                ShowPayInWalletButton = storeBlob.ShowPayInWalletButton,
                ShowStoreHeader = storeBlob.ShowStoreHeader,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob),
                HtmlTitle = storeBlob.HtmlTitle ?? "BTCPay Invoice",
                CelebratePayment = storeBlob.CelebratePayment,
                OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback,
                CryptoImage = Request.GetRelativePathOrAbsolute(GetPaymentMethodImage(paymentMethodId)),
                BtcAddress = prompt.Destination,
                BtcDue = ShowMoney(accounting.Due),
                BtcPaid = ShowMoney(accounting.Paid),
                InvoiceCurrency = invoice.Currency,
                // The Tweak is part of the PaymentMethodFee, but let's not show it in the UI as it's negligible.
                OrderAmount = ShowMoney(accounting.TotalDue - (prompt.PaymentMethodFee - prompt.TweakFee)),
                IsUnsetTopUp = invoice.IsUnsetTopUp(),
                CustomerEmail = invoice.Metadata.BuyerEmail,
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                DisplayExpirationTimer = (int)storeBlob.DisplayExpirationTimer.TotalSeconds,
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.Metadata.ItemDesc,
                Rate = ExchangeRate(prompt.Currency, prompt, DisplayFormatter.CurrencyFormat.Symbol),
                MerchantRefLink = invoice.RedirectURL?.AbsoluteUri ?? receiptUrl ?? "/",
                ReceiptLink = receiptUrl,
                RedirectAutomatically = invoice.RedirectAutomatically,
                StoreName = store.StoreName,
                StoreSupportUrl = supportUrl,
                TxCount = accounting.TxRequired,
                TxCountForFee = storeBlob.NetworkFeeMode switch
                {
                    NetworkFeeMode.Always => accounting.TxRequired,
                    NetworkFeeMode.MultiplePaymentsOnly => accounting.TxRequired - 1,
                    NetworkFeeMode.Never => 0,
                    _ => throw new NotImplementedException()
                },
                Status = invoice.Status.ToString(),
                // The Tweak is part of the PaymentMethodFee, but let's not show it in the UI as it's negligible.
                NetworkFee = prompt.PaymentMethodFee - prompt.TweakFee,
                IsMultiCurrency = invoice.GetPayments(false).Select(p => p.PaymentMethodId).Concat(new[] { prompt.PaymentMethodId }).Distinct().Count() > 1,
                StoreId = store.Id,
                AvailableCryptos = invoice.GetPaymentPrompts()
                                          .Select(kv =>
                                          {
                                              var handler = _handlers[kv.PaymentMethodId];
                                              return new PaymentModel.AvailableCrypto
                                              {
                                                  Displayed = displayedPaymentMethods.Contains(kv.PaymentMethodId),
                                                  PaymentMethodId = kv.PaymentMethodId.ToString(),
                                                  CryptoCode = kv.Currency,
                                                  PaymentMethodName = _prettyName.PrettyName(kv.PaymentMethodId),
                                                  IsLightning = handler is ILightningPaymentHandler,
                                                  CryptoImage = Request.GetRelativePathOrAbsolute(GetPaymentMethodImage(kv.PaymentMethodId)),
                                                  Link = Url.Action(nameof(Checkout),
                                                      new
                                                      {
                                                          invoiceId,
                                                          paymentMethodId = kv.PaymentMethodId.ToString()
                                                      })
                                              };
                                          }).Where(c => c.CryptoImage != "/")
                                          .OrderByDescending(a => a.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode).ThenBy(a => a.PaymentMethodName).ThenBy(a => a.IsLightning ? 1 : 0)
                                          .ToList()
            };
            if (_paymentModelExtensions.TryGetValue(paymentMethodId, out var extension))
                extension.ModifyPaymentModel(new PaymentModelContext(model, store, storeBlob, invoice, Url, prompt, handler));
            model.UISettings = _viewProvider.TryGetViewViewModel(prompt, "CheckoutUI")?.View as CheckoutUIPaymentMethodSettings;
            model.PaymentMethodId = paymentMethodId.ToString();
            model.OrderAmountFiat = OrderAmountFromInvoice(model.CryptoCode, invoice, DisplayFormatter.CurrencyFormat.Symbol);

            foreach (var paymentPrompt in invoice.GetPaymentPrompts())
            {
                var vvm = _viewProvider.TryGetViewViewModel(paymentPrompt, "CheckoutUI");
                if (vvm?.View is CheckoutUIPaymentMethodSettings { ExtensionPartial: { } partial })
                {
                    model.ExtensionPartials.Add(partial);
                }
            }

            if (storeBlob.PlaySoundOnPayment)
            {
                model.PaymentSoundUrl = storeBlob.PaymentSoundUrl is null
                    ? string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout/payment.mp3")
                    : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.PaymentSoundUrl);
                model.ErrorSoundUrl = string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout/error.mp3");
                model.NfcReadSoundUrl = string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout/nfcread.mp3");
            }

            var expiration = TimeSpan.FromSeconds(model.ExpirationSeconds);
            model.TimeLeft = expiration.PrettyPrint();
            return model;
        }

        private string? OrderAmountFromInvoice(string cryptoCode, InvoiceEntity invoiceEntity, DisplayFormatter.CurrencyFormat format = DisplayFormatter.CurrencyFormat.Code)
        {
            var currency = invoiceEntity.Currency;
            var crypto = cryptoCode.ToUpperInvariant(); // uppercase to make comparison easier, might be "sats"

            // if invoice source currency is the same as currently display currency, no need for "order amount from invoice"
            if (crypto == currency || (crypto == "SATS" && currency == "BTC") || (crypto == "BTC" && currency == "SATS"))
                return null;

            return _displayFormatter.Currency(invoiceEntity.Price, currency, format);
        }

        private string? ExchangeRate(string cryptoCode, PaymentPrompt paymentMethod, DisplayFormatter.CurrencyFormat format = DisplayFormatter.CurrencyFormat.Code)
        {
            var currency = paymentMethod.ParentEntity.Currency;
            var crypto = cryptoCode.ToUpperInvariant(); // uppercase to make comparison easier, might be "sats"

            if (crypto == currency || (crypto == "SATS" && currency == "BTC") || (crypto == "BTC" && currency == "SATS"))
                return null;

            return _displayFormatter.Currency(paymentMethod.Rate, currency, format);
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
            if (invoice == null || invoice.Status == InvoiceStatus.Settled || invoice.Status == InvoiceStatus.Invalid || invoice.Status == InvoiceStatus.Expired)
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

        [HttpGet("/stores/{storeId}/invoices")]
        [HttpGet("invoices")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewInvoices)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ListInvoices(InvoicesModel? model = null)
        {
            model = this.ParseListQuery(model ?? new InvoicesModel());
            var timezoneOffset = model.TimezoneOffset ?? 0;
            var searchTerm = string.IsNullOrEmpty(model.SearchText) ? model.SearchTerm : $"{model.SearchText},{model.SearchTerm}";
            var fs = new SearchString(searchTerm, timezoneOffset);
            string? storeId = model.StoreId;
            var storeIds = new HashSet<string>();
            if (storeId is not null)
            {
                storeIds.Add(storeId);
            }
            if (fs.GetFilterArray("storeid") is { } l)
            {
                foreach (var i in l)
                    storeIds.Add(i);
            }
            model.Search = fs;
            model.SearchText = fs.TextCombined;

            var apps = await _appService.GetAllApps(GetUserId(), false, storeId);
            InvoiceQuery invoiceQuery = GetInvoiceQuery(fs, apps, timezoneOffset);
            invoiceQuery.StoreId = storeIds.ToArray();
            invoiceQuery.Take = model.Count;
            invoiceQuery.Skip = model.Skip;
            invoiceQuery.IncludeRefunds = true;

            var list = await _InvoiceRepository.GetInvoices(invoiceQuery);

            // Apps
            model.Apps = apps.Select(a => new InvoiceAppModel
            {
                Id = a.Id,
                AppName = a.AppName,
                AppType = a.AppType
            }).ToList();

            foreach (var invoice in list)
            {
                var state = invoice.GetInvoiceState();
                model.Invoices.Add(new InvoiceModel
                {
                    Status = state,
                    ShowCheckout = invoice.Status == InvoiceStatus.New,
                    Date = invoice.InvoiceTime,
                    InvoiceId = invoice.Id,
                    OrderId = invoice.Metadata.OrderId ?? string.Empty,
                    RedirectUrl = invoice.RedirectURL?.AbsoluteUri ?? string.Empty,
                    Amount = invoice.Price,
                    Currency = invoice.Currency,
                    CanMarkInvalid = state.CanMarkInvalid(),
                    CanMarkSettled = state.CanMarkComplete(),
                    Details = InvoicePopulatePayments(invoice),
                    HasRefund = invoice.Refunds.Any()
                });
            }
            return View(model);
        }

        private InvoiceQuery GetInvoiceQuery(SearchString fs, ListAppsViewModel.ListAppViewModel[] apps, int timezoneOffset = 0)
        {
            var textSearch = fs.TextSearch;
            if (fs.GetFilterArray("appid") is { } appIds)
            {
                var appsById = apps.ToDictionary(a => a.Id);
                var searchTexts = appIds.Select(a => appsById.TryGet(a)).Where(a => a != null)
                    .Select(a => AppService.GetAppSearchTerm(a.AppType, a.Id))
                    .ToList();
                searchTexts.Add(fs.TextSearch);
                textSearch = string.Join(' ', searchTexts.Where(t => !string.IsNullOrEmpty(t)).ToList());
            }
            return new InvoiceQuery
            {
                TextSearch = textSearch,
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
        }

        [HttpGet("/stores/{storeId}/invoices/create")]
        [HttpGet("invoices/create")]
        [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(InvoicesModel? model = null)
        {
            if (string.IsNullOrEmpty(model?.StoreId))
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to select a store before creating an invoice.";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }

            var store = await _StoreRepository.FindStore(model.StoreId);
            if (store == null)
                return NotFound();

            if (!store.AnyPaymentMethodAvailable())
            {
                return NoPaymentMethodResult(store.Id);
            }

            var storeBlob = store.GetStoreBlob();
            var vm = new CreateInvoiceModel
            {
                StoreId = model.StoreId,
                Currency = storeBlob.DefaultCurrency,
                AvailablePaymentMethods = GetPaymentMethodsSelectList(store)
            };

            return View(vm);
        }

        [HttpPost("/stores/{storeId}/invoices/create")]
        [HttpPost("invoices/create")]
        [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model, CancellationToken cancellationToken)
        {
            var store = HttpContext.GetStoreData();
            if (!store.AnyPaymentMethodAvailable())
            {
                return NoPaymentMethodResult(store.Id);
            }

            var storeBlob = store.GetStoreBlob();
            model.AvailablePaymentMethods = GetPaymentMethodsSelectList(store);

            JObject? metadataObj = null;
            if (!string.IsNullOrEmpty(model.Metadata))
            {
                try
                {
                    metadataObj = JObject.Parse(model.Metadata);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(model.Metadata), "Metadata was not valid JSON");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            try
            {
                var metadata = metadataObj is null ? new InvoiceMetadata() : InvoiceMetadata.FromJObject(metadataObj);
                if (!string.IsNullOrEmpty(model.OrderId))
                {
                    metadata.OrderId = model.OrderId;
                }

                if (!string.IsNullOrEmpty(model.ItemDesc))
                {
                    metadata.ItemDesc = model.ItemDesc;
                }

                if (!string.IsNullOrEmpty(model.BuyerEmail))
                {
                    metadata.BuyerEmail = model.BuyerEmail;
                }

                var result = await CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                {
                    Amount = model.Amount,
                    Currency = model.Currency,
                    Metadata = metadata.ToJObject(),
                    Checkout = new()
                    {
                        RedirectURL = store.StoreWebsite,
                        DefaultPaymentMethod = model.DefaultPaymentMethod,
                        PaymentMethods = model.SupportedTransactionCurrencies?.ToArray()
                    },
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    entityManipulator: (entity) =>
                    {
                        entity.NotificationURLTemplate = model.NotificationUrl;
                        entity.FullNotifications = true;
                        entity.NotificationEmail = model.NotificationEmail;
                        entity.ExtendedNotifications = model.NotificationEmail != null;
                    },
                    cancellationToken: cancellationToken);

                TempData[WellKnownTempData.SuccessMessage] = $"Invoice {result.Id} just created!";
                CreatedInvoiceId = result.Id;

                return RedirectToAction(nameof(Invoice), new { storeId = result.StoreId, invoiceId = result.Id });
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
                model.StatusString = new InvoiceState(InvoiceStatus.Invalid, InvoiceExceptionStatus.Marked).ToString();
            }
            else if (newState == "settled")
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoiceId, InvoiceStatus.Settled);
                model.StatusString = new InvoiceState(InvoiceStatus.Settled, InvoiceExceptionStatus.Marked).ToString();
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

        private string GetUserId() => _UserManager.GetUserId(User)!;

        public class PosDataParser
        {
            public static Dictionary<string, object> ParsePosData(JToken? posData)
            {
                var result = new Dictionary<string, object>();
                if (posData is JObject jobj)
                {
                    foreach (var item in jobj)
                    {
                        ParsePosDataItem(item, ref result);
                    }
                }
                return result;
            }

            static void ParsePosDataItem(KeyValuePair<string, JToken?> item, ref Dictionary<string, object> result)
            {
                switch (item.Value?.Type)
                {
                    case JTokenType.Array:
                        var items = item.Value.AsEnumerable().ToList();
                        var arrayResult = new List<object>();
                        for (var i = 0; i < items.Count; i++)
                        {
                            arrayResult.Add(items[i] is JObject
                                ? ParsePosData(items[i])
                                : items[i].ToString());
                        }

                        result.TryAdd(item.Key, arrayResult);

                        break;
                    case JTokenType.Object:
                        result.TryAdd(item.Key, ParsePosData(item.Value));
                        break;
                    case null:
                        break;
                    default:
                        result.TryAdd(item.Key, item.Value.ToString());
                        break;
                }
            }
        }

        private SelectList GetPaymentMethodsSelectList(StoreData store)
        {
            var excludeFilter = store.GetStoreBlob().GetExcludedPaymentMethods();
            return new SelectList(store.GetPaymentMethodConfigs()
                    .Where(s => !excludeFilter.Match(s.Key))
                    .Select(method => new SelectListItem(method.Key.ToString(), method.Key.ToString())),
                nameof(SelectListItem.Value),
                nameof(SelectListItem.Text));
        }

        private IActionResult NoPaymentMethodResult(string storeId)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Html = $"To create an invoice, you need to <a href='{Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { cryptoCode = _NetworkProvider.DefaultNetwork.CryptoCode, storeId })}' class='alert-link'>set up a wallet</a> first",
                AllowDismiss = false
            });
            return RedirectToAction(nameof(ListInvoices), new { storeId });
        }
    }
}
