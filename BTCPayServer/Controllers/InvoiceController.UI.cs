using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.CoinSwitch;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Invoices.Export;
using DBriize.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
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

            var prodInfo = invoice.ProductInformation;
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            var model = new InvoiceDetailsModel()
            {
                StoreName = store.StoreName,
                StoreLink = Url.Action(nameof(StoresController.UpdateStore), "Stores", new { storeId = store.Id }),
                Id = invoice.Id,
                State = invoice.GetInvoiceState().ToString(),
                TransactionSpeed = invoice.SpeedPolicy == SpeedPolicy.HighSpeed ? "high" :
                                   invoice.SpeedPolicy == SpeedPolicy.MediumSpeed ? "medium" :
                                   invoice.SpeedPolicy == SpeedPolicy.LowMediumSpeed ? "low-medium" :
                                   "low",
                RefundEmail = invoice.RefundMail,
                CreatedDate = invoice.InvoiceTime,
                ExpirationDate = invoice.ExpirationTime,
                MonitoringDate = invoice.MonitoringExpiration,
                OrderId = invoice.OrderId,
                BuyerInformation = invoice.BuyerInformation,
                Fiat = _CurrencyNameTable.DisplayFormatCurrency(prodInfo.Price, prodInfo.Currency),
                TaxIncluded = _CurrencyNameTable.DisplayFormatCurrency(prodInfo.TaxIncluded, prodInfo.Currency),
                NotificationUrl = invoice.NotificationURL?.AbsoluteUri,
                RedirectUrl = invoice.RedirectURL?.AbsoluteUri,
                ProductInformation = invoice.ProductInformation,
                StatusException = invoice.ExceptionStatus,
                Events = invoice.Events,
                PosData = PosDataParser.ParsePosData(invoice.PosData),
                Archived = invoice.Archived,
                CanRefund = CanRefund(invoice.GetInvoiceState()),
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
            return invoiceState.Status == InvoiceStatus.Confirmed ||
                invoiceState.Status == InvoiceStatus.Complete ||
                (invoiceState.Status == InvoiceStatus.Expired &&
                (invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidLate ||
                invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidOver ||
                invoiceState.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)) ||
                invoiceState.Status == InvoiceStatus.Invalid;
        }

        [HttpGet]
        [Route("invoices/{invoiceId}/refund")]
        [AllowAnonymous]
        public async Task<IActionResult> Refund(string invoiceId, CancellationToken cancellationToken)
        {
            using var ctx = _dbContextFactory.CreateContext();
            ctx.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            var invoice = await ctx.Invoices.Include(i => i.Payments)
                                            .Include(i => i.CurrentRefund)
                                            .Include(i => i.CurrentRefund.PullPaymentData)
                                            .Where(i => i.Id == invoiceId)
                                            .FirstOrDefaultAsync();
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
                var options = paymentMethods
                    .Select(o => o.GetId())
                    .Select(o => o.CryptoCode)
                    .Where(o => _NetworkProvider.GetNetwork<BTCPayNetwork>(o) is BTCPayNetwork n && !n.ReadonlyWallet)
                    .Distinct()
                    .OrderBy(o => o)
                    .Select(o => new PaymentMethodId(o, PaymentTypes.BTCLike))
                    .ToList();
                var defaultRefund = invoice.Payments
                    .Select(p => p.GetBlob(_NetworkProvider))
                    .Select(p => p?.GetPaymentMethodId())
                    .FirstOrDefault(p => p != null && p.PaymentType == BitcoinPaymentType.Instance);
                // TODO: What if no option?
                var refund = new RefundModel();
                refund.Title = "Select a payment method";
                refund.AvailablePaymentMethods = new SelectList(options, nameof(PaymentMethodId.CryptoCode), nameof(PaymentMethodId.CryptoCode));
                refund.SelectedPaymentMethod = defaultRefund?.ToString() ?? options.Select(o => o.CryptoCode).First();

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
            model.RefundStep = RefundSteps.SelectRate;
            using var ctx = _dbContextFactory.CreateContext();
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice is null)
                return NotFound();
            var store = await _StoreRepository.FindStore(invoice.StoreId, GetUserId());
            if (store is null)
                return NotFound();
            if (!CanRefund(invoice.GetInvoiceState()))
                return NotFound();
            var paymentMethodId = new PaymentMethodId(model.SelectedPaymentMethod, PaymentTypes.BTCLike);
            var cdCurrency = _CurrencyNameTable.GetCurrencyData(invoice.ProductInformation.Currency, true);
            var paymentMethodDivisibility = _CurrencyNameTable.GetCurrencyData(paymentMethodId.CryptoCode, false)?.Divisibility ?? 8;
            if (model.SelectedRefundOption is null)
            {
                model.Title = "What to refund?";
                var paymentMethod = invoice.GetPaymentMethods()[paymentMethodId];
                var paidCurrency = Math.Round(paymentMethod.Calculate().Paid.ToDecimal(MoneyUnit.BTC) * paymentMethod.Rate, cdCurrency.Divisibility);
                model.CryptoAmountThen = Math.Round(paidCurrency / paymentMethod.Rate, paymentMethodDivisibility);
                model.RateThenText = _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountThen, paymentMethodId.CryptoCode, true);
                var rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
                var rateResult = await _RateProvider.FetchRate(new Rating.CurrencyPair(paymentMethodId.CryptoCode, invoice.ProductInformation.Currency), rules, cancellationToken);
                //TODO: What if fetching rate failed?
                if (rateResult.BidAsk is null)
                {
                    ModelState.AddModelError(nameof(model.SelectedRefundOption), $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                    return View(model);
                }
                model.CryptoAmountNow = Math.Round(paidCurrency / rateResult.BidAsk.Bid, paymentMethodDivisibility);
                model.CurrentRateText = _CurrencyNameTable.DisplayFormatCurrency(model.CryptoAmountNow, paymentMethodId.CryptoCode, true);
                model.FiatAmount = paidCurrency;
                model.FiatText = _CurrencyNameTable.DisplayFormatCurrency(model.FiatAmount, invoice.ProductInformation.Currency, true);
                return View(model);
            }
            else
            {
                var createPullPayment = new HostedServices.CreatePullPayment();
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
                        createPullPayment.Currency = invoice.ProductInformation.Currency;
                        createPullPayment.Amount = model.FiatAmount;
                        break;
                    default:
                        ModelState.AddModelError(nameof(model.SelectedRefundOption), "Invalid choice");
                        return View(model);
                }
                var ppId = await _paymentHostedService.CreatePullPayment(createPullPayment);
                this.TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html = "Share this page with a customer so they can claim a refund <br />Once claimed you need to initiate a refund from Wallet > Payouts",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                (await ctx.Invoices.FindAsync(invoice.Id)).CurrentRefundId = ppId;
                ctx.Refunds.Add(new RefundData()
                {
                    InvoiceDataId = invoice.Id,
                    PullPaymentDataId = ppId
                });
                await ctx.SaveChangesAsync();
                // TODO: Having dedicated UI later on
                return RedirectToAction(nameof(PullPaymentController.ViewPullPayment),
                                "PullPayment",
                                new { pullPaymentId = ppId });
            }
        }

        private InvoiceDetailsModel InvoicePopulatePayments(InvoiceEntity invoice)
        {
            var model = new InvoiceDetailsModel();
            model.Archived = invoice.Archived;
            model.Payments = invoice.GetPayments();
            foreach (var data in invoice.GetPaymentMethods())
            {
                var accounting = data.Calculate();
                var paymentMethodId = data.GetId();
                var cryptoPayment = new InvoiceDetailsModel.CryptoPayment();

                cryptoPayment.PaymentMethodId = paymentMethodId;
                cryptoPayment.PaymentMethod = paymentMethodId.ToPrettyString();
                cryptoPayment.Due = _CurrencyNameTable.DisplayFormatCurrency(accounting.Due.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode);
                cryptoPayment.Paid = _CurrencyNameTable.DisplayFormatCurrency(accounting.CryptoPaid.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode);
                cryptoPayment.Overpaid = _CurrencyNameTable.DisplayFormatCurrency(accounting.OverpaidHelper.ToDecimal(MoneyUnit.BTC), paymentMethodId.CryptoCode);
                var paymentMethodDetails = data.GetPaymentMethodDetails();
                cryptoPayment.Address = paymentMethodDetails.GetPaymentDestination();
                cryptoPayment.Rate = ExchangeRate(data);
                model.CryptoPayments.Add(cryptoPayment);
            }
            return model;
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
        public async Task<IActionResult> Checkout(string invoiceId, string id = null, string paymentMethodId = null,
            [FromQuery] string view = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            id = invoiceId;
            //

            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId));
            if (model == null)
                return NotFound();

            if (view == "modal")
                model.IsModal = true;

            _CSP.Add(new ConsentSecurityPolicy("script-src", "'unsafe-eval'")); // Needed by Vue
            if (!string.IsNullOrEmpty(model.CustomCSSLink) &&
                Uri.TryCreate(model.CustomCSSLink, UriKind.Absolute, out var uri))
            {
                _CSP.Clear();
            }

            if (!string.IsNullOrEmpty(model.CustomLogoLink) &&
                Uri.TryCreate(model.CustomLogoLink, UriKind.Absolute, out uri))
            {
                _CSP.Clear();
            }

            return View(nameof(Checkout), model);
        }

        [HttpGet]
        [Route("invoice-noscript")]
        public async Task<IActionResult> CheckoutNoScript(string invoiceId, string id = null, string paymentMethodId = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            id = invoiceId;
            //

            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId));
            if (model == null)
                return NotFound();

            return View(model);
        }

        private async Task<PaymentModel> GetInvoiceModel(string invoiceId, PaymentMethodId paymentMethodId)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice == null)
                return null;
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            bool isDefaultPaymentId = false;
            if (paymentMethodId == null)
            {
                paymentMethodId = store.GetDefaultPaymentId(_NetworkProvider);
                isDefaultPaymentId = true;
            }
            BTCPayNetworkBase network = _NetworkProvider.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
            if (network == null && isDefaultPaymentId)
            {
                //TODO: need to look into a better way for this as it does not scale
                network = _NetworkProvider.GetAll().OfType<BTCPayNetwork>().FirstOrDefault();
                paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            }
            if (invoice == null || network == null)
                return null;
            if (!invoice.Support(paymentMethodId))
            {
                if (!isDefaultPaymentId)
                    return null;
                var paymentMethodTemp = invoice.GetPaymentMethods()
                                               .Where(c => paymentMethodId.CryptoCode == c.GetId().CryptoCode)
                                               .FirstOrDefault();
                if (paymentMethodTemp == null)
                    paymentMethodTemp = invoice.GetPaymentMethods().First();
                network = paymentMethodTemp.Network;
                paymentMethodId = paymentMethodTemp.GetId();
            }

            var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
            var paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
            var dto = invoice.EntityToDTO();
            var cryptoInfo = dto.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var storeBlob = store.GetStoreBlob();
            var currency = invoice.ProductInformation.Currency;
            var accounting = paymentMethod.Calculate();

            ChangellySettings changelly = (storeBlob.ChangellySettings != null && storeBlob.ChangellySettings.Enabled &&
                                           storeBlob.ChangellySettings.IsConfigured())
                ? storeBlob.ChangellySettings
                : null;

            CoinSwitchSettings coinswitch = (storeBlob.CoinSwitchSettings != null && storeBlob.CoinSwitchSettings.Enabled &&
                                           storeBlob.CoinSwitchSettings.IsConfigured())
                ? storeBlob.CoinSwitchSettings
                : null;


            var changellyAmountDue = changelly != null
                ? (accounting.Due.ToDecimal(MoneyUnit.BTC) *
                   (1m + (changelly.AmountMarkupPercentage / 100m)))
                : (decimal?)null;

            var paymentMethodHandler = _paymentMethodHandlerDictionary[paymentMethodId];

            var divisibility = _CurrencyNameTable.GetNumberFormatInfo(paymentMethod.GetId().CryptoCode, false)?.CurrencyDecimalDigits;

            var model = new PaymentModel()
            {
                CryptoCode = network.CryptoCode,
                RootPath = this.Request.PathBase.Value.WithTrailingSlash(),
                OrderId = invoice.OrderId,
                InvoiceId = invoice.Id,
                DefaultLang = storeBlob.DefaultLang ?? "en",
                CustomCSSLink = storeBlob.CustomCSS,
                CustomLogoLink = storeBlob.CustomLogo,
                HtmlTitle = storeBlob.HtmlTitle ?? "BTCPay Invoice",
                CryptoImage = Request.GetRelativePathOrAbsolute(paymentMethodHandler.GetCryptoImage(paymentMethodId)),
                BtcAddress = paymentMethodDetails.GetPaymentDestination(),
                BtcDue = accounting.Due.ShowMoney(divisibility),
                OrderAmount = (accounting.TotalDue - accounting.NetworkFee).ShowMoney(divisibility),
                OrderAmountFiat = OrderAmountFromInvoice(network.CryptoCode, invoice.ProductInformation),
                CustomerEmail = invoice.RefundMail,
                RequiresRefundEmail = storeBlob.RequiresRefundEmail,
                ShowRecommendedFee = storeBlob.ShowRecommendedFee,
                FeeRate = paymentMethodDetails.GetFeeRate(),
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.ProductInformation.ItemDesc,
                Rate = ExchangeRate(paymentMethod),
                MerchantRefLink = invoice.RedirectURL?.AbsoluteUri ?? "/",
                RedirectAutomatically = invoice.RedirectAutomatically,
                StoreName = store.StoreName,
                PeerInfo = (paymentMethodDetails as LightningLikePaymentMethodDetails)?.NodeInfo,
                TxCount = accounting.TxRequired,
                BtcPaid = accounting.Paid.ShowMoney(divisibility),
#pragma warning disable CS0618 // Type or member is obsolete
                Status = invoice.StatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                NetworkFee = paymentMethodDetails.GetNextNetworkFee(),
                IsMultiCurrency = invoice.GetPayments().Select(p => p.GetPaymentMethodId()).Concat(new[] { paymentMethod.GetId() }).Distinct().Count() > 1,
                ChangellyEnabled = changelly != null,
                ChangellyMerchantId = changelly?.ChangellyMerchantId,
                ChangellyAmountDue = changellyAmountDue,
                CoinSwitchEnabled = coinswitch != null,
                CoinSwitchAmountMarkupPercentage = coinswitch?.AmountMarkupPercentage ?? 0,
                CoinSwitchMerchantId = coinswitch?.MerchantId,
                CoinSwitchMode = coinswitch?.Mode,
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

            paymentMethodHandler.PreparePaymentModel(model, dto, storeBlob);
            if (model.IsLightning && storeBlob.LightningAmountInSatoshi && model.CryptoCode == "Sats")
            {
                model.Rate = _CurrencyNameTable.DisplayFormatCurrency(paymentMethod.Rate / 100_000_000, paymentMethod.ParentEntity.ProductInformation.Currency);
            }
            model.UISettings = paymentMethodHandler.GetCheckoutUISettings();
            model.PaymentMethodId = paymentMethodId.ToString();
            var expiration = TimeSpan.FromSeconds(model.ExpirationSeconds);
            model.TimeLeft = expiration.PrettyPrint();
            return model;
        }

        private string OrderAmountFromInvoice(string cryptoCode, ProductInformation productInformation)
        {
            // if invoice source currency is the same as currently display currency, no need for "order amount from invoice"
            if (cryptoCode == productInformation.Currency)
                return null;

            return _CurrencyNameTable.DisplayFormatCurrency(productInformation.Price, productInformation.Currency);
        }
        private string ExchangeRate(PaymentMethod paymentMethod)
        {
            string currency = paymentMethod.ParentEntity.ProductInformation.Currency;
            return _CurrencyNameTable.DisplayFormatCurrency(paymentMethod.Rate, currency);
        }

        [HttpGet]
        [Route("i/{invoiceId}/status")]
        [Route("i/{invoiceId}/{paymentMethodId}/status")]
        [Route("invoice/{invoiceId}/status")]
        [Route("invoice/{invoiceId}/{paymentMethodId}/status")]
        [Route("invoice/status")]
        public async Task<IActionResult> GetStatus(string invoiceId, string paymentMethodId = null)
        {
            var model = await GetInvoiceModel(invoiceId, paymentMethodId == null ? null : PaymentMethodId.Parse(paymentMethodId));
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
        public async Task<IActionResult> GetStatusWebSocket(string invoiceId)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            if (invoice == null || invoice.Status == InvoiceStatus.Complete || invoice.Status == InvoiceStatus.Invalid || invoice.Status == InvoiceStatus.Expired)
                return NotFound();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            CompositeDisposable leases = new CompositeDisposable();
            try
            {
                leases.Add(_EventAggregator.Subscribe<Events.InvoiceDataChangedEvent>(async o => await NotifySocket(webSocket, o.InvoiceId, invoiceId)));
                leases.Add(_EventAggregator.Subscribe<Events.InvoiceNewAddressEvent>(async o => await NotifySocket(webSocket, o.InvoiceId, invoiceId)));
                leases.Add(_EventAggregator.Subscribe<Events.InvoiceEvent>(async o => await NotifySocket(webSocket, o.Invoice.Id, invoiceId)));
                while (true)
                {
                    var message = await webSocket.ReceiveAsync(DummyBuffer, default(CancellationToken));
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
        public async Task<IActionResult> ListInvoices(InvoicesModel model = null)
        {
            model = this.ParseListQuery(model ?? new InvoicesModel());

            var fs = new SearchString(model.SearchTerm);
            var storeIds = fs.GetFilterArray("storeid") != null ? fs.GetFilterArray("storeid") : new List<string>().ToArray();

            model.StoreIds = storeIds;

            InvoiceQuery invoiceQuery = GetInvoiceQuery(model.SearchTerm, model.TimezoneOffset ?? 0);
            var counting = _InvoiceRepository.GetInvoicesTotal(invoiceQuery);
            invoiceQuery.Count = model.Count;
            invoiceQuery.Skip = model.Skip;
            var list = await _InvoiceRepository.GetInvoices(invoiceQuery);

            foreach (var invoice in list)
            {
                var state = invoice.GetInvoiceState();
                model.Invoices.Add(new InvoiceModel()
                {
                    Status = invoice.Status,
                    StatusString = state.ToString(),
                    ShowCheckout = invoice.Status == InvoiceStatus.New,
                    Date = invoice.InvoiceTime,
                    InvoiceId = invoice.Id,
                    OrderId = invoice.OrderId ?? string.Empty,
                    RedirectUrl = invoice.RedirectURL?.AbsoluteUri ?? string.Empty,
                    AmountCurrency = _CurrencyNameTable.DisplayFormatCurrency(invoice.ProductInformation.Price, invoice.ProductInformation.Currency),
                    CanMarkInvalid = state.CanMarkInvalid(),
                    CanMarkComplete = state.CanMarkComplete(),
                    Details = InvoicePopulatePayments(invoice),
                });
            }
            model.Total = await counting;
            return View(model);
        }

        private InvoiceQuery GetInvoiceQuery(string searchTerm = null, int timezoneOffset = 0)
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
        public async Task<IActionResult> Export(string format, string searchTerm = null, int timezoneOffset = 0)
        {
            var model = new InvoiceExport(_CurrencyNameTable);

            InvoiceQuery invoiceQuery = GetInvoiceQuery(searchTerm, timezoneOffset);
            invoiceQuery.Skip = 0;
            invoiceQuery.Count = int.MaxValue;
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
        public async Task<IActionResult> CreateInvoice()
        {
            var stores = new SelectList(await _StoreRepository.GetStoresByUserId(GetUserId()), nameof(StoreData.Id), nameof(StoreData.StoreName), null);
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
                ModelState.AddModelError(nameof(model.StoreId), "You need to configure the derivation scheme in order to create an invoice");
                return View(model);
            }

            try
            {
                var result = await CreateInvoiceCore(new CreateInvoiceRequest()
                {
                    Price = model.Amount.Value,
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
                    })
                }, store, HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);

                TempData[WellKnownTempData.SuccessMessage] = $"Invoice {result.Data.Id} just created!";
                return RedirectToAction(nameof(ListInvoices));
            }
            catch (BitpayHttpException ex)
            {
                ModelState.TryAddModelError(nameof(model.Currency), $"Error: {ex.Message}");
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
                await _InvoiceRepository.UpdatePaidInvoiceToInvalid(invoiceId);
                _EventAggregator.Publish(new InvoiceEvent(invoice, 1008, InvoiceEvent.MarkedInvalid));
                model.StatusString = new InvoiceState("invalid", "marked").ToString();
            }
            else if (newState == "complete")
            {
                await _InvoiceRepository.UpdatePaidInvoiceToComplete(invoiceId);
                _EventAggregator.Publish(new InvoiceEvent(invoice, 2008, InvoiceEvent.MarkedCompleted));
                model.StatusString = new InvoiceState("complete", "marked").ToString();
            }

            return Json(model);
        }

        public class InvoiceStateChangeModel
        {
            public bool NotFound { get; set; }
            public string StatusString { get; set; }
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

                        switch (item.Value.Type)
                        {
                            case JTokenType.Array:
                                var items = item.Value.AsEnumerable().ToList();
                                for (var i = 0; i < items.Count; i++)
                                {
                                    result.Add($"{item.Key}[{i}]", ParsePosData(items[i].ToString()));
                                }
                                break;
                            case JTokenType.Object:
                                result.Add(item.Key, ParsePosData(item.Value.ToString()));
                                break;
                            default:
                                result.Add(item.Key, item.Value.ToString());
                                break;
                        }

                    }
                }
                catch
                {
                    result.Add(string.Empty, posData);
                }
                return result;
            }
        }

    }
}
