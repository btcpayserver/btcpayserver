using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using System.Net.WebSockets;
using System.Threading;
using BTCPayServer.Events;
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
                UserId = GetUserId(),
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
                TransactionSpeed = invoice.SpeedPolicy == SpeedPolicy.HighSpeed ? "high" : invoice.SpeedPolicy == SpeedPolicy.MediumSpeed ? "medium" : "low",
                RefundEmail = invoice.RefundMail,
                CreatedDate = invoice.InvoiceTime,
                ExpirationDate = invoice.ExpirationTime,
                MonitoringDate = invoice.MonitoringExpiration,
                OrderId = invoice.OrderId,
                BuyerInformation = invoice.BuyerInformation,
                Fiat = FormatCurrency((decimal)dto.Price, dto.Currency),
                NotificationUrl = invoice.NotificationURL,
                RedirectUrl = invoice.RedirectURL,
                ProductInformation = invoice.ProductInformation,
                StatusException = invoice.ExceptionStatus,
                Events = invoice.Events
            };

            foreach (var data in invoice.GetCryptoData(null))
            {
                var cryptoInfo = dto.CryptoInfo.First(o => o.CryptoCode.Equals(data.Key, StringComparison.OrdinalIgnoreCase));
                var accounting = data.Value.Calculate();
                var paymentNetwork = _NetworkProvider.GetNetwork(data.Key);
                var cryptoPayment = new InvoiceDetailsModel.CryptoPayment();
                cryptoPayment.CryptoCode = paymentNetwork.CryptoCode;
                cryptoPayment.Due = accounting.Due.ToString() + $" {paymentNetwork.CryptoCode}";
                cryptoPayment.Paid = accounting.CryptoPaid.ToString() + $" {paymentNetwork.CryptoCode}";
                cryptoPayment.Address = data.Value.DepositAddress.ToString();
                cryptoPayment.Rate = FormatCurrency(data.Value);
                cryptoPayment.PaymentUrl = cryptoInfo.PaymentUrls.BIP21;
                model.CryptoPayments.Add(cryptoPayment);
            }

            var payments = invoice
                .GetPayments()
                .Select(async payment =>
                {
                    var m = new InvoiceDetailsModel.Payment();
                    var paymentNetwork = _NetworkProvider.GetNetwork(payment.GetCryptoCode());
                    m.CryptoCode = payment.GetCryptoCode();
                    m.DepositAddress = payment.GetScriptPubKey().GetDestinationAddress(paymentNetwork.NBitcoinNetwork);
                    m.Confirmations = (await _ExplorerClients.GetExplorerClient(payment.GetCryptoCode())?.GetTransactionAsync(payment.Outpoint.Hash))?.Confirmations ?? 0;
                    m.TransactionId = payment.Outpoint.Hash.ToString();
                    m.ReceivedTime = payment.ReceivedTime;
                    m.TransactionLink = string.Format(paymentNetwork.BlockExplorerLink, m.TransactionId);
                    m.Replaced = !payment.Accounted;
                    return m;
                })
                .ToArray();
            await Task.WhenAll(payments);
            model.Addresses = invoice.HistoricalAddresses;
            model.Payments = payments.Select(p => p.GetAwaiter().GetResult()).ToList();
            model.StatusMessage = StatusMessage;
            return View(model);
        }

        [HttpGet]
        [Route("i/{invoiceId}")]
        [Route("i/{invoiceId}/{cryptoCode}")]
        [Route("invoice")]
        [AcceptMediaTypeConstraint("application/bitcoin-paymentrequest", false)]
        [XFrameOptionsAttribute(null)]
        public async Task<IActionResult> Checkout(string invoiceId, string id = null, string cryptoCode = null)
        {
            //Keep compatibility with Bitpay
            invoiceId = invoiceId ?? id;
            id = invoiceId;
            ////

            var model = await GetInvoiceModel(invoiceId, cryptoCode);
            if (model == null)
                return NotFound();

            return View(nameof(Checkout), model);
        }

        private async Task<PaymentModel> GetInvoiceModel(string invoiceId, string cryptoCode)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            if (invoice == null)
                return null;
            var store = await _StoreRepository.FindStore(invoice.StoreId);
            bool isDefaultCrypto = false;
            if (cryptoCode == null)
            { 
                cryptoCode = store.GetDefaultCrypto();
                isDefaultCrypto = true;
            }
            var network = _NetworkProvider.GetNetwork(cryptoCode);
            if (invoice == null || network == null)
                return null;

            if(!invoice.Support(network))
            {
                if(!isDefaultCrypto)
                    return null;
                network = invoice.GetCryptoData(_NetworkProvider).First().Value.Network;
            }
            var cryptoData = invoice.GetCryptoData(network, _NetworkProvider);

            var dto = invoice.EntityToDTO(_NetworkProvider);
            var cryptoInfo = dto.CryptoInfo.First(o => o.CryptoCode == network.CryptoCode);

            var currency = invoice.ProductInformation.Currency;
            var accounting = cryptoData.Calculate();
            var model = new PaymentModel()
            {
                CryptoCode = network.CryptoCode,
                ServerUrl = HttpContext.Request.GetAbsoluteRoot(),
                OrderId = invoice.OrderId,
                InvoiceId = invoice.Id,
                BtcAddress = cryptoData.DepositAddress,
                OrderAmount = (accounting.TotalDue - accounting.NetworkFee).ToString(),
                BtcDue = accounting.Due.ToString(),
                CustomerEmail = invoice.RefundMail,
                ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
                MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
                MaxTimeMinutes = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalMinutes,
                ItemDesc = invoice.ProductInformation.ItemDesc,
                Rate = FormatCurrency(cryptoData),
                MerchantRefLink = invoice.RedirectURL ?? "/",
                StoreName = store.StoreName,
                InvoiceBitcoinUrl = cryptoInfo.PaymentUrls.BIP21,
                TxCount = accounting.TxCount,
                BtcPaid = accounting.Paid.ToString(),
                Status = invoice.Status,
                CryptoImage = "/" + Url.Content(network.CryptoImagePath),
                NetworkFeeDescription = $"{accounting.TxCount} transaction{(accounting.TxCount > 1 ? "s" : "")} x {cryptoData.TxFee} {network.CryptoCode}",
                AvailableCryptos = invoice.GetCryptoData(_NetworkProvider)
                                          .Where(i => i.Value.Network != null)
                                          .Select(kv=> new PaymentModel.AvailableCrypto()
                                            {
                                                CryptoCode = kv.Key,
                                                CryptoImage = "/" + kv.Value.Network.CryptoImagePath,
                                                Link = Url.Action(nameof(Checkout), new { invoiceId = invoiceId, cryptoCode = kv.Key })
                                            }).Where(c => c.CryptoImage != "/")
                .ToList()
            };

            var isMultiCurrency = invoice.GetPayments().Select(p=>p.GetCryptoCode()).Concat(new[] { network.CryptoCode }).Distinct().Count() > 1;
            if (isMultiCurrency)
                model.NetworkFeeDescription = $"{accounting.NetworkFee} {network.CryptoCode}";

            var expiration = TimeSpan.FromSeconds(model.ExpirationSeconds);
            model.TimeLeft = PrettyPrint(expiration);
            return model;
        }

        private string FormatCurrency(CryptoData cryptoData)
        {
            string currency = cryptoData.ParentEntity.ProductInformation.Currency;
            return FormatCurrency(cryptoData.Rate, currency);
        }
        public string FormatCurrency(decimal price, string currency)
        {
            return price.ToString("C", _CurrencyNameTable.GetCurrencyProvider(currency)) + $" ({currency})";
        }

        private string PrettyPrint(TimeSpan expiration)
        {
            StringBuilder builder = new StringBuilder();
            if (expiration.Days >= 1)
                builder.Append(expiration.Days.ToString());
            if (expiration.Hours >= 1)
                builder.Append(expiration.Hours.ToString("00"));
            builder.Append($"{expiration.Minutes.ToString("00")}:{expiration.Seconds.ToString("00")}");
            return builder.ToString();
        }

        [HttpGet]
        [Route("i/{invoiceId}/status")]
        [Route("i/{invoiceId}/{cryptoCode}/status")]
        public async Task<IActionResult> GetStatus(string invoiceId, string cryptoCode)
        {
            var model = await GetInvoiceModel(invoiceId, cryptoCode);
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
                leases.Add(_EventAggregator.Subscribe<Events.InvoiceEvent>(async o => await NotifySocket(webSocket, o.InvoiceId, invoiceId)));
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
        [Authorize(AuthenticationSchemes = "Identity.Application")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> ListInvoices(string searchTerm = null, int skip = 0, int count = 20)
        {
            var model = new InvoicesModel();
            var filterString = new SearchString(searchTerm);
            foreach (var invoice in await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                TextSearch = filterString.TextSearch,
                Count = count,
                Skip = skip,
                UserId = GetUserId(),
                Status = filterString.Filters.TryGet("status"),
                StoreId = filterString.Filters.TryGet("storeid")
            }))
            {
                model.SearchTerm = searchTerm;
                model.Invoices.Add(new InvoiceModel()
                {
                    Status = invoice.Status,
                    Date = invoice.InvoiceTime,
                    InvoiceId = invoice.Id,
                    AmountCurrency = $"{invoice.ProductInformation.Price.ToString(CultureInfo.InvariantCulture)} {invoice.ProductInformation.Currency}"
                });
            }
            model.Skip = skip;
            model.Count = count;
            model.StatusMessage = StatusMessage;
            return View(model);
        }

        [HttpGet]
        [Route("invoices/create")]
        [Authorize(AuthenticationSchemes = "Identity.Application")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice()
        {
            var stores = await GetStores(GetUserId());
            if (stores.Count() == 0)
            {
                StatusMessage = "Error: You need to create at least one store before creating a transaction";
                return RedirectToAction(nameof(StoresController.ListStores), "Stores");
            }
            return View(new CreateInvoiceModel() { Stores = stores });
        }

        [HttpPost]
        [Route("invoices/create")]
        [Authorize(AuthenticationSchemes = "Identity.Application")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model)
        {
            model.Stores = await GetStores(GetUserId(), model.StoreId);
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var store = await _StoreRepository.FindStore(model.StoreId, GetUserId());
            if (store.GetDerivationStrategies(_NetworkProvider).Count() == 0)
            {
                StatusMessage = "Error: You need to configure the derivation scheme in order to create an invoice";
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
                    NotificationURL = model.NotificationUrl,
                    ItemDesc = model.ItemDesc,
                    FullNotifications = true,
                    BuyerEmail = model.BuyerEmail,
                }, store, HttpContext.Request.GetAbsoluteRoot());

                StatusMessage = $"Invoice {result.Data.Id} just created!";
                return RedirectToAction(nameof(ListInvoices));
            }
            catch (RateUnavailableException)
            {
                ModelState.TryAddModelError(nameof(model.Currency), "Unsupported currency");
                return View(model);
            }
        }

        private async Task<SelectList> GetStores(string userId, string storeId = null)
        {
            return new SelectList(await _StoreRepository.GetStoresByUserId(userId), nameof(StoreData.Id), nameof(StoreData.StoreName), storeId);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = "Identity.Application")]
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
        [Authorize(AuthenticationSchemes = "Identity.Application")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> InvalidatePaidInvoice(string invoiceId)
        {
            await _InvoiceRepository.UpdatePaidInvoiceToInvalid(invoiceId);
            _EventAggregator.Publish(new InvoiceEvent(invoiceId, 1008, "invoice_markedInvalid"));
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
