#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Net.WebSockets;
using System.Reflection.Metadata;
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
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;
using BitpayCreateInvoiceRequest = BTCPayServer.Models.BitpayCreateInvoiceRequest;
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
                IncludeEvents = true,
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
                RefundEmail = invoice.RefundMail,
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
                Events = invoice.Events,
                Metadata = metaData,
                Archived = invoice.Archived,
                HasRefund = invoice.Refunds.Any(),
                CanRefund = invoiceState.CanRefund(),
                Refunds = invoice.Refunds,
                ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
                ShowReceipt = invoice.Status.ToModernStatus() == InvoiceStatus.Settled && (invoice.ReceiptOptions?.Enabled ?? receipt.Enabled is true),
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
            
            if (additionalData.ContainsKey("receiptData"))
            {
                model.ReceiptData = (Dictionary<string, object>)additionalData["receiptData"];
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
                if (i.RedirectURL is not null)
                {
                    return Redirect(i.RedirectURL.ToString());
                }   
                return NotFound();

            }
            var storeBlob = store.GetStoreBlob();
            var vm = new InvoiceReceiptViewModel
            {
                InvoiceId = i.Id,
                OrderId = i.Metadata?.OrderId,
                OrderUrl = i.Metadata?.OrderUrl,
                Status = i.Status.ToModernStatus(),
                Currency = i.Currency,
                Timestamp = i.InvoiceTime,
                StoreName = store.StoreName,
                StoreBranding = new StoreBrandingViewModel(storeBlob),
                ReceiptOptions = receipt
            };

            if (i.Status.ToModernStatus() != InvoiceStatus.Settled)
            {
                return View(vm);
            }

            JToken? receiptData = null;
            i.Metadata?.AdditionalData?.TryGetValue("receiptData", out receiptData);

            var payments = ViewPaymentRequestViewModel.PaymentRequestInvoicePayment.GetViewModels(i, _displayFormatter, _transactionLinkProviders);

            vm.Amount = i.PaidAmount.Net;
            vm.Payments = receipt.ShowPayments is false ? null : payments;
            vm.AdditionalData = PosDataParser.ParsePosData(receiptData);

            return View(print ? "InvoiceReceiptPrint" : "InvoiceReceipt", vm);
        }

        [HttpGet("invoices/{invoiceId}/refund")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Refund([FromServices] IEnumerable<IPayoutHandler> payoutHandlers, string invoiceId, CancellationToken cancellationToken)
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

            var paymentMethods = invoice.GetBlob(_NetworkProvider).GetPaymentMethods();
            var pmis = paymentMethods.Select(method => method.GetId()).ToList();
            pmis = pmis.Concat(pmis.Where(id => id.PaymentType == LNURLPayPaymentType.Instance)
                .Select(id => new PaymentMethodId(id.CryptoCode, LightningPaymentType.Instance))).ToList();
            var relevant = payoutHandlers.Where(handler => pmis.Any(handler.CanHandle));
            var options = (await relevant.GetSupportedPaymentMethods(invoice.StoreData)).Where(id => pmis.Contains(id)).ToList();
            if (!options.Any())
            {
                var vm = new RefundModel { Title = "No matching payment method" };
                ModelState.AddModelError(nameof(vm.AvailablePaymentMethods),
                    "There are no payment methods available to provide refunds with for this invoice.");
                return View("_RefundModal", vm);
            }

            var defaultRefund = invoice.Payments
                .Select(p => p.GetBlob(_NetworkProvider))
                .Select(p => p?.GetPaymentMethodId())
                .FirstOrDefault(p => p != null && options.Contains(p));

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
            var paymentMethodId = PaymentMethodId.Parse(model.SelectedPaymentMethod);
            var cdCurrency = _CurrencyNameTable.GetCurrencyData(invoice.Currency, true);
            var paymentMethodDivisibility = _CurrencyNameTable.GetCurrencyData(paymentMethodId.CryptoCode, false)?.Divisibility ?? 8;
            RateRules rules;
            RateResult rateResult;
            CreatePullPayment createPullPayment;
            PaymentMethodAccounting accounting;
            var pms = invoice.GetPaymentMethods();
            var paymentMethod = pms.SingleOrDefault(method => method.GetId() == paymentMethodId);
            var appliedDivisibility = paymentMethodDivisibility;
            decimal dueAmount = default;
            decimal paidAmount = default;
            decimal cryptoPaid = default;

            //TODO: Make this clean
            if (paymentMethod is null && paymentMethodId.PaymentType == LightningPaymentType.Instance)
            {
                paymentMethod = pms[new PaymentMethodId(paymentMethodId.CryptoCode, PaymentTypes.LNURLPay)];
            }

            if (paymentMethod != null)
            {
                accounting = paymentMethod.Calculate();
                cryptoPaid = accounting.Paid;
                dueAmount = accounting.TotalDue;
                paidAmount = cryptoPaid.RoundToSignificant(appliedDivisibility);
            }

            var isPaidOver = invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver;
            decimal? overpaidAmount = isPaidOver ? Math.Round(paidAmount - dueAmount, appliedDivisibility) : null;

            switch (model.RefundStep)
            {
                case RefundSteps.SelectPaymentMethod:
                    model.RefundStep = RefundSteps.SelectRate;
                    model.Title = "How much to refund?";

                    if (paymentMethod != null && cryptoPaid != default)
                    {
                        var paidCurrency = Math.Round(cryptoPaid * paymentMethod.Rate, cdCurrency.Divisibility);
                        model.CryptoAmountThen = cryptoPaid.RoundToSignificant(paymentMethodDivisibility);
                        model.RateThenText = _displayFormatter.Currency(model.CryptoAmountThen, paymentMethodId.CryptoCode);
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
                        model.CurrentRateText = _displayFormatter.Currency(model.CryptoAmountNow, paymentMethodId.CryptoCode);
                        model.FiatAmount = paidCurrency;
                    }
                    model.CryptoCode = paymentMethodId.CryptoCode;
                    model.CryptoDivisibility = paymentMethodDivisibility;
                    model.InvoiceDivisibility = cdCurrency.Divisibility;
                    model.InvoiceCurrency = invoice.Currency;
                    model.CustomAmount = model.FiatAmount;
                    model.CustomCurrency = invoice.Currency;
                    model.SubtractPercentage = 0;
                    model.OverpaidAmount = overpaidAmount;
                    model.OverpaidAmountText = overpaidAmount != null ? _displayFormatter.Currency(overpaidAmount.Value, paymentMethodId.CryptoCode) : null;
                    model.FiatText = _displayFormatter.Currency(model.FiatAmount, invoice.Currency);
                    return View("_RefundModal", model);

                case RefundSteps.SelectRate:
                    createPullPayment = new CreatePullPayment
                    {
                        Name = $"Refund {invoice.Id}",
                        PaymentMethodIds = new[] { paymentMethodId },
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
                            createPullPayment.Currency = paymentMethodId.CryptoCode;
                            createPullPayment.Amount = model.CryptoAmountThen;
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove;
                            break;

                        case "CurrentRate":
                            createPullPayment.Currency = paymentMethodId.CryptoCode;
                            createPullPayment.Amount = model.CryptoAmountNow;
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove;
                            break;

                        case "Fiat":
                            appliedDivisibility = cdCurrency.Divisibility;
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
                    
                            createPullPayment.Currency = paymentMethodId.CryptoCode;
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
                            createPullPayment.AutoApproveClaims = authorizedForAutoApprove && paymentMethodId.CryptoCode == model.CustomCurrency;
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
                createPullPayment.Amount = Math.Round(createPullPayment.Amount - reduceByAmount, appliedDivisibility);
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
                CryptoPayments = invoice.GetPaymentMethods().Select(
                    data =>
                    {
                        var accounting = data.Calculate();
                        var paymentMethodId = data.GetId();
                        var hasPayment = accounting.CryptoPaid > 0;
                        var overpaidAmount = accounting.OverpaidHelper;
                        var rate = ExchangeRate(data.GetId().CryptoCode, data);
                        
                        if (rate is not null) hasRates = true;
                        if (hasPayment && overpaidAmount > 0) overpaid = true;
                        if (hasPayment && accounting.Due > 0) stillDue = true;

                        return new InvoiceDetailsModel.CryptoPayment
                        {
                            Rate = rate,
                            PaymentMethodRaw = data,
                            PaymentMethodId = paymentMethodId,
                            PaymentMethod = paymentMethodId.ToPrettyString(),
                            TotalDue = _displayFormatter.Currency(accounting.TotalDue, paymentMethodId.CryptoCode),
                            Due = hasPayment ? _displayFormatter.Currency(accounting.Due, paymentMethodId.CryptoCode) : null,
                            Paid = hasPayment ? _displayFormatter.Currency(accounting.CryptoPaid, paymentMethodId.CryptoCode) : null,
                            Overpaid = hasPayment ? _displayFormatter.Currency(overpaidAmount, paymentMethodId.CryptoCode) : null,
                            Address = data.GetPaymentMethodDetails().GetPaymentDestination()
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
                case "cpfp":
                    var network = _NetworkProvider.DefaultNetwork;
                    var explorer = _ExplorerClients.GetExplorerClient(network);
                    if (explorer is null)
                        return NotSupported("This feature is only available to BTC wallets");
                    if (!GetCurrentStore().HasPermission(GetUserId(), Policies.CanModifyStoreSettings))
                        return Forbid();

                    var derivationScheme = (this.GetCurrentStore().GetDerivationSchemeSettings(_NetworkProvider, network.CryptoCode))?.AccountDerivation;
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
                return NotFound();

            if (view == "modal")
                model.IsModal = true;

            var viewName = model.CheckoutType == CheckoutType.V2 ? "CheckoutV2" : nameof(Checkout);
            return View(viewName, model);
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
            var btcId = PaymentMethodId.Parse("BTC");
            var lnId = PaymentMethodId.Parse("BTC_LightningLike");
            var lnurlId = PaymentMethodId.Parse("BTC_LNURLPAY");


            var displayedPaymentMethods = invoice.GetPaymentMethods().Select(p => p.GetId()).ToList();

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
                PaymentMethodId? invoicePaymentId = invoice.GetDefaultPaymentMethod();
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
                    paymentMethodId = displayedPaymentMethods.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.BTCLike) ??
                                      displayedPaymentMethods.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType != PaymentTypes.LNURLPay) ??
                                      displayedPaymentMethods.FirstOrDefault();
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
                    .Where(p => displayedPaymentMethods.Contains(p.GetId()))
                    .FirstOrDefault();
                if (paymentMethodTemp is null)
                    return null;
                network = paymentMethodTemp.Network;
                paymentMethodId = paymentMethodTemp.GetId();
            }


            // We activate the default payment method, and also those which aren't displayed (as they can't be set as default)
            bool activated = false;
            foreach (var pm in invoice.GetPaymentMethods())
            {
                var pmi = pm.GetId();
                if (pmi != paymentMethodId || !displayedPaymentMethods.Contains(pmi))
                    continue;
                var pmd = pm.GetPaymentMethodDetails();
                if (!pmd.Activated)
                {
                    if (await _invoiceActivator.ActivateInvoicePaymentMethod(pmi, invoice, store))
                    {
                        activated = true;
                    }
                }
            }
            if (activated)
                return await GetInvoiceModel(invoiceId, paymentMethodId, lang);


            var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
            var paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
            var dto = invoice.EntityToDTO();
            var accounting = paymentMethod.Calculate();
            var paymentMethodHandler = _paymentMethodHandlerDictionary[paymentMethodId];

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

            var isAltcoinsBuild = false;
#if ALTCOINS
            isAltcoinsBuild = true;
#endif

            var orderId = invoice.Metadata.OrderId;
            var supportUrl = !string.IsNullOrEmpty(storeBlob.StoreSupportUrl)
                ? storeBlob.StoreSupportUrl
                    .Replace("{OrderId}", string.IsNullOrEmpty(orderId) ? string.Empty : Uri.EscapeDataString(orderId))
                    .Replace("{InvoiceId}", Uri.EscapeDataString(invoice.Id))
                : null;

            var model = new PaymentModel
            {
                Activated = paymentMethodDetails.Activated,
                CryptoCode = network.CryptoCode,
                RootPath = Request.PathBase.Value.WithTrailingSlash(),
                OrderId = orderId,
                InvoiceId = invoiceId,
                DefaultLang = lang ?? invoice.DefaultLanguage ?? storeBlob.DefaultLang ?? "en",
                ShowPayInWalletButton = storeBlob.ShowPayInWalletButton,
                ShowStoreHeader = storeBlob.ShowStoreHeader,
                StoreBranding = new StoreBrandingViewModel(storeBlob)
                {
                    CustomCSSLink = storeBlob.CustomCSS
                },
                CustomLogoLink = storeBlob.CustomLogo,
                CheckoutType = invoice.CheckoutType ?? storeBlob.CheckoutType,
                HtmlTitle = storeBlob.HtmlTitle ?? "BTCPay Invoice",
                CelebratePayment = storeBlob.CelebratePayment,
                OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback,
                CryptoImage = Request.GetRelativePathOrAbsolute(paymentMethodHandler.GetCryptoImage(paymentMethodId)),
                BtcAddress = paymentMethodDetails.GetPaymentDestination(),
                BtcDue = accounting.ShowMoney(accounting.Due),
                BtcPaid = accounting.ShowMoney(accounting.Paid),
                InvoiceCurrency = invoice.Currency,
                OrderAmount = accounting.ShowMoney(accounting.TotalDue - accounting.NetworkFee),
                IsUnsetTopUp = invoice.IsUnsetTopUp(),
                CustomerEmail = invoice.RefundMail,
                RequiresRefundEmail = invoice.RequiresRefundEmail ?? storeBlob.RequiresRefundEmail,
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                DisplayExpirationTimer = (int)storeBlob.DisplayExpirationTimer.TotalSeconds,
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.Metadata.ItemDesc,
                Rate = ExchangeRate(network.CryptoCode, paymentMethod, DisplayFormatter.CurrencyFormat.Symbol),
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
                RequiredConfirmations = invoice.SpeedPolicy switch
                {
                    SpeedPolicy.HighSpeed => 0,
                    SpeedPolicy.MediumSpeed => 1,
                    SpeedPolicy.LowMediumSpeed => 2,
                    SpeedPolicy.LowSpeed => 6,
                    _ => null
                },
                ReceivedConfirmations = invoice.GetAllBitcoinPaymentData(false).FirstOrDefault()?.ConfirmationCount,
#pragma warning disable CS0618 // Type or member is obsolete
                Status = invoice.StatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                NetworkFee = paymentMethodDetails.GetNextNetworkFee(),
                IsMultiCurrency = invoice.GetPayments(false).Select(p => p.GetPaymentMethodId()).Concat(new[] { paymentMethod.GetId() }).Distinct().Count() > 1,
                StoreId = store.Id,
                AvailableCryptos = invoice.GetPaymentMethods()
                                          .Select(kv =>
                                          {
                                              var availableCryptoPaymentMethodId = kv.GetId();
                                              var availableCryptoHandler = _paymentMethodHandlerDictionary[availableCryptoPaymentMethodId];
                                              var pmName = availableCryptoHandler.GetPaymentMethodName(availableCryptoPaymentMethodId);
                                              return new PaymentModel.AvailableCrypto
                                              {
                                                  Displayed = displayedPaymentMethods.Contains(kv.GetId()),
                                                  PaymentMethodId = kv.GetId().ToString(),
                                                  CryptoCode = kv.Network?.CryptoCode ?? kv.GetId().CryptoCode,
                                                  PaymentMethodName = isAltcoinsBuild
                                                      ? pmName
                                                      : pmName.Replace("Bitcoin (", "").Replace(")", "").Replace("Lightning ", ""),
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
            model.PaymentType = paymentMethodId.PaymentType.ToString();
            model.OrderAmountFiat = OrderAmountFromInvoice(model.CryptoCode, invoice, DisplayFormatter.CurrencyFormat.Symbol);

            if (storeBlob.PlaySoundOnPayment)
            {
                model.PaymentSoundUrl = string.IsNullOrEmpty(storeBlob.SoundFileId)
                    ? string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout-v2/payment.mp3")
                    : await _fileService.GetFileUrl(Request.GetAbsoluteRootUri(), storeBlob.SoundFileId);
                model.ErrorSoundUrl = string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout-v2/error.mp3");
                model.NfcReadSoundUrl = string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout-v2/nfcread.mp3");
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

        private string? ExchangeRate(string cryptoCode, PaymentMethod paymentMethod, DisplayFormatter.CurrencyFormat format = DisplayFormatter.CurrencyFormat.Code)
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
            model.SearchText = fs.TextSearch;

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
                    ShowCheckout = invoice.Status == InvoiceStatusLegacy.New,
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

            var storeBlob = HttpContext.GetStoreData()?.GetStoreBlob();
            var vm = new CreateInvoiceModel
            {
                StoreId = model.StoreId,
                Currency = storeBlob?.DefaultCurrency,
                CheckoutType = storeBlob?.CheckoutType ?? CheckoutType.V2,
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
            var store = HttpContext.GetStoreData();
            var storeBlob = store.GetStoreBlob();
            model.CheckoutType = storeBlob.CheckoutType;
            model.AvailablePaymentMethods = GetPaymentMethodsSelectList();

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
                    Checkout = new ()
                    {
                        RedirectURL = store.StoreWebsite,
                        DefaultPaymentMethod = model.DefaultPaymentMethod,
                        RequiresRefundEmail = model.RequiresRefundEmail == RequiresRefundEmail.InheritFromStore
                            ? storeBlob.RequiresRefundEmail
                            : model.RequiresRefundEmail == RequiresRefundEmail.On,
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
    }
}
