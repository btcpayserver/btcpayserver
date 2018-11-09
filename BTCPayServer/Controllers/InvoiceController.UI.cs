using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
        [HttpGet]
        [Route("invoices/{invoiceId}")]
        public async Task<IActionResult> Invoice(string invoiceId)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = invoiceId,
                IncludeAddresses = true,
                IncludeEvents = true
            })).FirstOrDefault();
            if (invoice == null)
                return NotFound();

            var dto = invoice.EntityToDTO(_NetworkProvider);
            var store = await _StoreRepository.FindStore(invoice.StoreId);

            InvoiceDetailsModel model = new InvoiceDetailsModel()
            {
                StoreName = store.StoreName,
                StoreLink = Url.Action(nameof(StoresController.UpdateStore), "Stores", new { storeId = store.Id }),
                Id = invoice.Id,
                Status = invoice.Status,
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
                Fiat = _CurrencyNameTable.DisplayFormatCurrency((decimal)dto.Price, dto.Currency),
                NotificationEmail = invoice.NotificationEmail,
                NotificationUrl = invoice.NotificationURL,
                RedirectUrl = invoice.RedirectURL,
                ProductInformation = invoice.ProductInformation,
                StatusException = invoice.ExceptionStatus,
                Events = invoice.Events
            };

            foreach (var data in invoice.GetPaymentMethods(null))
            {
                var cryptoInfo = dto.CryptoInfo.First(o => o.GetpaymentMethodId() == data.GetId());
                var accounting = data.Calculate();
                var paymentMethodId = data.GetId();
                var cryptoPayment = new InvoiceDetailsModel.CryptoPayment();
                cryptoPayment.PaymentMethod = ToString(paymentMethodId);
                cryptoPayment.Due = accounting.Due.ToString() + $" {paymentMethodId.CryptoCode}";
                cryptoPayment.Paid = accounting.CryptoPaid.ToString() + $" {paymentMethodId.CryptoCode}";
                cryptoPayment.Overpaid = (accounting.DueUncapped > Money.Zero ? Money.Zero : -accounting.DueUncapped).ToString() + $" {paymentMethodId.CryptoCode}";

                var onchainMethod = data.GetPaymentMethodDetails() as Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod;
                if (onchainMethod != null)
                {
                    cryptoPayment.Address = onchainMethod.DepositAddress;
                }
                cryptoPayment.Rate = ExchangeRate(data);
                cryptoPayment.PaymentUrl = cryptoInfo.PaymentUrls.BIP21;
                model.CryptoPayments.Add(cryptoPayment);
            }

            var onChainPayments = invoice
                .GetPayments()
                .Select<PaymentEntity, Task<object>>(async payment =>
                {
                    var paymentNetwork = _NetworkProvider.GetNetwork(payment.GetCryptoCode());
                    var paymentData = payment.GetCryptoPaymentData();
                    if (paymentData is Payments.Bitcoin.BitcoinLikePaymentData onChainPaymentData)
                    {
                        var m = new InvoiceDetailsModel.Payment();
                        m.Crypto = payment.GetPaymentMethodId().CryptoCode;
                        m.DepositAddress = onChainPaymentData.Output.ScriptPubKey.GetDestinationAddress(paymentNetwork.NBitcoinNetwork);

                        int confirmationCount = 0;
                        if ((onChainPaymentData.ConfirmationCount < paymentNetwork.MaxTrackedConfirmation && payment.Accounted)
                             && (onChainPaymentData.Legacy || invoice.MonitoringExpiration < DateTimeOffset.UtcNow)) // The confirmation count in the paymentData is not up to date
                        {
                            confirmationCount = (await ((ExplorerClientProvider)_ServiceProvider.GetService(typeof(ExplorerClientProvider))).GetExplorerClient(payment.GetCryptoCode())?.GetTransactionAsync(onChainPaymentData.Outpoint.Hash))?.Confirmations ?? 0;
                            onChainPaymentData.ConfirmationCount = confirmationCount;
                            payment.SetCryptoPaymentData(onChainPaymentData);
                            await _InvoiceRepository.UpdatePayments(new List<PaymentEntity> { payment });
                        }
                        else
                        {
                            confirmationCount = onChainPaymentData.ConfirmationCount;
                        }
                        if (confirmationCount >= paymentNetwork.MaxTrackedConfirmation)
                        {
                            m.Confirmations = "At least " + (paymentNetwork.MaxTrackedConfirmation);
                        }
                        else
                        {
                            m.Confirmations = confirmationCount.ToString(CultureInfo.InvariantCulture);
                        }

                        m.TransactionId = onChainPaymentData.Outpoint.Hash.ToString();
                        m.ReceivedTime = payment.ReceivedTime;
                        m.TransactionLink = string.Format(CultureInfo.InvariantCulture, paymentNetwork.BlockExplorerLink, m.TransactionId);
                        m.Replaced = !payment.Accounted;
                        return m;
                    }
                    else
                    {
                        var lightningPaymentData = (Payments.Lightning.LightningLikePaymentData)paymentData;
                        return new InvoiceDetailsModel.OffChainPayment()
                        {
                            Crypto = paymentNetwork.CryptoCode,
                            BOLT11 = lightningPaymentData.BOLT11
                        };
                    }
                })
                .ToArray();
            await Task.WhenAll(onChainPayments);
            model.Addresses = invoice.HistoricalAddresses.Select(h => new InvoiceDetailsModel.AddressModel
            {
                Destination = h.GetAddress(),
                PaymentMethod = ToString(h.GetPaymentMethodId()),
                Current = !h.UnAssigned.HasValue
            }).ToArray();
            model.OnChainPayments = onChainPayments.Select(p => p.GetAwaiter().GetResult()).OfType<InvoiceDetailsModel.Payment>().ToList();
            model.OffChainPayments = onChainPayments.Select(p => p.GetAwaiter().GetResult()).OfType<InvoiceDetailsModel.OffChainPayment>().ToList();
            model.StatusMessage = StatusMessage;
            return View(model);
        }

        private string ToString(PaymentMethodId paymentMethodId)
        {
            var type = paymentMethodId.PaymentType.ToString();
            switch (paymentMethodId.PaymentType)
            {
                case PaymentTypes.BTCLike:
                    type = "On-Chain";
                    break;
                case PaymentTypes.LightningLike:
                    type = "Off-Chain";
                    break;
            }
            return $"{paymentMethodId.CryptoCode} ({type})";
        }

        [HttpGet]
        [Route("i/{invoiceId}")]
        [Route("i/{invoiceId}/{paymentMethodId}")]
        [Route("invoice")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest", false)]
        [XFrameOptionsAttribute(null)]
        [ReferrerPolicyAttribute("origin")]
        public async Task<IActionResult> Checkout(string invoiceId, string id = null, string paymentMethodId = null,
            [FromQuery]string view = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            id = invoiceId;
            ////

            var model = await GetInvoiceModel(invoiceId, paymentMethodId);
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

        private async Task<PaymentModel> GetInvoiceModel(string invoiceId, string paymentMethodIdStr)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            if (invoice == null)
                return null;
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            bool isDefaultCrypto = false;
            if (paymentMethodIdStr == null)
            {
                paymentMethodIdStr = store.GetDefaultCrypto(_NetworkProvider);
                isDefaultCrypto = true;
            }
            var paymentMethodId = PaymentMethodId.Parse(paymentMethodIdStr);
            var network = _NetworkProvider.GetNetwork(paymentMethodId.CryptoCode);
            if (network == null && isDefaultCrypto)
            {
                network = _NetworkProvider.GetAll().FirstOrDefault();
                paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
                paymentMethodIdStr = paymentMethodId.ToString();
            }
            if (invoice == null || network == null)
                return null;
            if (!invoice.Support(paymentMethodId))
            {
                if (!isDefaultCrypto)
                    return null;
                var paymentMethodTemp = invoice.GetPaymentMethods(_NetworkProvider)
                                               .Where(c => paymentMethodId.CryptoCode == c.GetId().CryptoCode)
                                               .FirstOrDefault();
                if (paymentMethodTemp == null)
                    paymentMethodTemp = invoice.GetPaymentMethods(_NetworkProvider).First();
                network = paymentMethodTemp.Network;
                paymentMethodId = paymentMethodTemp.GetId();
                paymentMethodIdStr = paymentMethodId.ToString();
            }

            var paymentMethod = invoice.GetPaymentMethod(paymentMethodId, _NetworkProvider);
            var paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
            var dto = invoice.EntityToDTO(_NetworkProvider);
            var cryptoInfo = dto.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var storeBlob = store.GetStoreBlob();
            var currency = invoice.ProductInformation.Currency;
            var accounting = paymentMethod.Calculate();

            ChangellySettings changelly = (storeBlob.ChangellySettings != null && storeBlob.ChangellySettings.Enabled &&
                                           storeBlob.ChangellySettings.IsConfigured())
                ? storeBlob.ChangellySettings
                : null;


            var changellyAmountDue = changelly != null
                ? (accounting.Due.ToDecimal(MoneyUnit.BTC) *
                   (1m + (changelly.AmountMarkupPercentage / 100m)))
                : (decimal?)null;

            var model = new PaymentModel()
            {
                CryptoCode = network.CryptoCode,
                PaymentMethodId = paymentMethodId.ToString(),
                PaymentMethodName = GetDisplayName(paymentMethodId, network),
                CryptoImage = GetImage(paymentMethodId, network),
                IsLightning = paymentMethodId.PaymentType == PaymentTypes.LightningLike,
                ServerUrl = HttpContext.Request.GetAbsoluteRoot(),
                OrderId = invoice.OrderId,
                InvoiceId = invoice.Id,
                DefaultLang = storeBlob.DefaultLang ?? "en",
                HtmlTitle = storeBlob.HtmlTitle ?? "BTCPay Invoice",
                CustomCSSLink = storeBlob.CustomCSS?.AbsoluteUri,
                CustomLogoLink = storeBlob.CustomLogo?.AbsoluteUri,
                BtcAddress = paymentMethodDetails.GetPaymentDestination(),
                BtcDue = accounting.Due.ToString(),
                OrderAmount = (accounting.TotalDue - accounting.NetworkFee).ToString(),
                OrderAmountFiat = OrderAmountFromInvoice(network.CryptoCode, invoice.ProductInformation),
                CustomerEmail = invoice.RefundMail,
                RequiresRefundEmail = storeBlob.RequiresRefundEmail,
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.ProductInformation.ItemDesc,
                Rate = ExchangeRate(paymentMethod),
                MerchantRefLink = invoice.RedirectURL ?? "/",
                StoreName = store.StoreName,
                InvoiceBitcoinUrl = paymentMethodId.PaymentType == PaymentTypes.BTCLike ? cryptoInfo.PaymentUrls.BIP21 :
                                    paymentMethodId.PaymentType == PaymentTypes.LightningLike ? cryptoInfo.PaymentUrls.BOLT11 :
                                    throw new NotSupportedException(),
                PeerInfo = (paymentMethodDetails as LightningLikePaymentMethodDetails)?.NodeInfo,
                InvoiceBitcoinUrlQR = paymentMethodId.PaymentType == PaymentTypes.BTCLike ? cryptoInfo.PaymentUrls.BIP21 :
                                    paymentMethodId.PaymentType == PaymentTypes.LightningLike ? cryptoInfo.PaymentUrls.BOLT11.ToUpperInvariant() :
                                    throw new NotSupportedException(),
                TxCount = accounting.TxRequired,
                BtcPaid = accounting.Paid.ToString(),
                Status = invoice.Status,
                NetworkFee = paymentMethodDetails.GetTxFee(),
                IsMultiCurrency = invoice.GetPayments().Select(p => p.GetPaymentMethodId()).Concat(new[] { paymentMethod.GetId() }).Distinct().Count() > 1,
                ChangellyEnabled = changelly != null,
                ChangellyMerchantId = changelly?.ChangellyMerchantId,
                ChangellyAmountDue = changellyAmountDue,
                StoreId = store.Id,
                AvailableCryptos = invoice.GetPaymentMethods(_NetworkProvider)
                                          .Where(i => i.Network != null)
                                          .Select(kv => new PaymentModel.AvailableCrypto()
                                          {
                                              PaymentMethodId = kv.GetId().ToString(),
                                              CryptoCode = kv.GetId().CryptoCode,
                                              PaymentMethodName = GetDisplayName(kv.GetId(), kv.Network),
                                              IsLightning = kv.GetId().PaymentType == PaymentTypes.LightningLike,
                                              CryptoImage = GetImage(kv.GetId(), kv.Network),
                                              Link = Url.Action(nameof(Checkout), new { invoiceId = invoiceId, paymentMethodId = kv.GetId().ToString() })
                                          }).Where(c => c.CryptoImage != "/")
                                          .OrderByDescending(a => a.CryptoCode == "BTC").ThenBy(a => a.PaymentMethodName).ThenBy(a => a.IsLightning ? 1 : 0)
                                          .ToList()
            };

            var expiration = TimeSpan.FromSeconds(model.ExpirationSeconds);
            model.TimeLeft = expiration.PrettyPrint();
            return model;
        }

        private string GetDisplayName(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            return paymentMethodId.PaymentType == PaymentTypes.BTCLike ?
                network.DisplayName : network.DisplayName + " (Lightning)";
        }

        private string GetImage(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            var res = paymentMethodId.PaymentType == PaymentTypes.BTCLike ?
                Url.Content(network.CryptoImagePath) : Url.Content(network.LightningImagePath);
            return "/" + res;
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
        public async Task<IActionResult> GetStatus(string invoiceId, string paymentMethodId = null)
        {
            var model = await GetInvoiceModel(invoiceId, paymentMethodId);
            if (model == null)
                return NotFound();
            return Json(model);
        }

        [HttpGet]
        [Route("i/{invoiceId}/status/ws")]
        public async Task<IActionResult> GetStatusWebSocket(string invoiceId)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            if (invoice == null || invoice.Status == "complete" || invoice.Status == "invalid" || invoice.Status == "expired")
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
            finally
            {
                leases.Dispose();
                await webSocket.CloseSocket();
            }
            return new EmptyResult();
        }

        ArraySegment<Byte> DummyBuffer = new ArraySegment<Byte>(new Byte[1]);
        private async Task NotifySocket(WebSocket webSocket, string invoiceId, string expectedId)
        {
            if (invoiceId != expectedId || webSocket.State != WebSocketState.Open)
                return;
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            try
            {
                await webSocket.SendAsync(DummyBuffer, WebSocketMessageType.Binary, true, cts.Token);
            }
            catch { try { webSocket.Dispose(); } catch { } }
        }

        [HttpPost]
        [Route("i/{invoiceId}/UpdateCustomer")]
        public async Task<IActionResult> UpdateCustomer(string invoiceId, [FromBody]UpdateCustomerModel data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            await _InvoiceRepository.UpdateInvoice(invoiceId, data).ConfigureAwait(false);
            return Ok();
        }

        [HttpGet]
        [Route("invoices")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ListInvoices(string searchTerm = null, int skip = 0, int count = 50)
        {
            var model = new InvoicesModel
            {
                SearchTerm = searchTerm,
                Skip = skip,
                Count = count,
                StatusMessage = StatusMessage
            };

            var list = await ListInvoicesProcess(searchTerm, skip, count);
            foreach (var invoice in list)
            {
                model.Invoices.Add(new InvoiceModel()
                {
                    Status = invoice.Status + (invoice.ExceptionStatus == null ? string.Empty : $" ({invoice.ExceptionStatus})"),
                    ShowCheckout = invoice.Status == "new",
                    Date = invoice.InvoiceTime,
                    InvoiceId = invoice.Id,
                    OrderId = invoice.OrderId ?? string.Empty,
                    RedirectUrl = invoice.RedirectURL ?? string.Empty,
                    AmountCurrency = $"{invoice.ProductInformation.Price.ToString(CultureInfo.InvariantCulture)} {invoice.ProductInformation.Currency}"
                });
            }
            return View(model);
        }

        private async Task<InvoiceEntity[]> ListInvoicesProcess(string searchTerm = null, int skip = 0, int count = 50)
        {
            var filterString = new SearchString(searchTerm);
            var list = await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                TextSearch = filterString.TextSearch,
                Count = count,
                Skip = skip,
                UserId = GetUserId(),
                Unusual = !filterString.Filters.ContainsKey("unusual") ? null
                          : !bool.TryParse(filterString.Filters["unusual"].First(), out var r) ? (bool?)null
                          : r,
                Status = filterString.Filters.ContainsKey("status") ? filterString.Filters["status"].ToArray() : null,
                ExceptionStatus = filterString.Filters.ContainsKey("exceptionstatus") ? filterString.Filters["exceptionstatus"].ToArray() : null,
                StoreId = filterString.Filters.ContainsKey("storeid") ? filterString.Filters["storeid"].ToArray() : null
            });

            return list;
        }

        [HttpGet]
        [Route("invoices/create")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice()
        {
            var stores = new SelectList(await _StoreRepository.GetStoresByUserId(GetUserId()), nameof(StoreData.Id), nameof(StoreData.StoreName), null);
            if (stores.Count() == 0)
            {
                StatusMessage = "Error: You need to create at least one store before creating a transaction";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
            }
            return View(new CreateInvoiceModel() { Stores = stores });
        }

        [HttpPost]
        [Route("invoices/create")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model)
        {
            var stores = await _StoreRepository.GetStoresByUserId(GetUserId());
            model.Stores = new SelectList(stores, nameof(StoreData.Id), nameof(StoreData.StoreName), model.StoreId);
            var store = stores.FirstOrDefault(s => s.Id == model.StoreId);
            if (store == null)
            {
                ModelState.AddModelError(nameof(model.StoreId), "Store not found");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            StatusMessage = null;
            if (!store.HasClaim(Policies.CanCreateInvoice.Key))
            {
                ModelState.AddModelError(nameof(model.StoreId), "You need to be owner of this store to create an invoice");
                return View(model);
            }

            if (store.GetSupportedPaymentMethods(_NetworkProvider).Count() == 0)
            {
                ModelState.AddModelError(nameof(model.StoreId), "You need to configure the derivation scheme in order to create an invoice");
                return View(model);
            }

            if (StatusMessage != null)
            {
                return RedirectToAction(nameof(StoresController.UpdateStore), "Stores", new
                {
                    storeId = store.Id
                });
            }

            try
            {
                var result = await CreateInvoiceCore(new Invoice()
                {
                    Price = model.Amount.Value,
                    Currency = model.Currency,
                    PosData = model.PosData,
                    OrderId = model.OrderId,
                    //RedirectURL = redirect + "redirect",
                    NotificationEmail = model.NotificationEmail,
                    NotificationURL = model.NotificationUrl,
                    ItemDesc = model.ItemDesc,
                    FullNotifications = true,
                    BuyerEmail = model.BuyerEmail,
                }, store, HttpContext.Request.GetAbsoluteRoot());

                StatusMessage = $"Invoice {result.Data.Id} just created!";
                return RedirectToAction(nameof(ListInvoices));
            }
            catch (BitpayHttpException ex)
            {
                ModelState.TryAddModelError(nameof(model.Currency), $"Error: {ex.Message}");
                return View(model);
            }
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        [BitpayAPIConstraint(false)]
        public IActionResult SearchInvoice(InvoicesModel invoices)
        {
            return RedirectToAction(nameof(ListInvoices), new
            {
                searchTerm = invoices.SearchTerm,
                skip = invoices.Skip,
                count = invoices.Count,
            });
        }

        [HttpPost]
        [Route("invoices/invalidatepaid")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> InvalidatePaidInvoice(string invoiceId)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            if (invoice == null)
                return NotFound();
            await _InvoiceRepository.UpdatePaidInvoiceToInvalid(invoiceId);
            _EventAggregator.Publish(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1008, "invoice_markedInvalid"));
            return RedirectToAction(nameof(ListInvoices));
        }

        [TempData]
        public string StatusMessage
        {
            get;
            set;
        }

        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }
}
