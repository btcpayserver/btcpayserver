using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using NBitcoin.Crypto;
using Newtonsoft.Json;
using MarkPayoutRequest = BTCPayServer.HostedServices.MarkPayoutRequest;

namespace BTCPayServer
{
    [Route("~/{cryptoCode}/[controller]/")]
    [Route("~/{cryptoCode}/lnurl/")]
    public class UILNURLController : Controller
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;

        private readonly UIInvoiceController _invoiceController;
        private readonly LinkGenerator _linkGenerator;
        private readonly LightningAddressService _lightningAddressService;
        private readonly LightningLikePayoutHandler _lightningLikePayoutHandler;
        private readonly PullPaymentHostedService _pullPaymentHostedService;

        public UILNURLController(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider btcPayNetworkProvider,
            LightningLikePaymentHandler lightningLikePaymentHandler,
            StoreRepository storeRepository,
            AppService appService,
            UIInvoiceController invoiceController,
            LinkGenerator linkGenerator,
            LightningAddressService lightningAddressService,
            LightningLikePayoutHandler lightningLikePayoutHandler,
            PullPaymentHostedService pullPaymentHostedService)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningLikePaymentHandler = lightningLikePaymentHandler;
            _storeRepository = storeRepository;
            _appService = appService;
            _invoiceController = invoiceController;
            _linkGenerator = linkGenerator;
            _lightningAddressService = lightningAddressService;
            _lightningLikePayoutHandler = lightningLikePayoutHandler;
            _pullPaymentHostedService = pullPaymentHostedService;
        }


        [HttpGet("withdraw/pp/{pullPaymentId}")]
        public async Task<IActionResult> GetLNURLForPullPayment(string cryptoCode, string pullPaymentId, string pr)
        {
            
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null || !network.SupportLightning)
            {
                return NotFound();
            }

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var pp = await _pullPaymentHostedService.GetPullPayment(pullPaymentId, true);
            if (!pp.IsRunning() || !pp.IsSupported(pmi))
            {
                return NotFound();
            }

            var blob = pp.GetBlob();
            if (!blob.Currency.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase))
            {
                return NotFound();
            }

            var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, DateTimeOffset.UtcNow);
           
            var remaining = progress.Limit - progress.Completed - progress.Awaiting;
            var request = new LNURLWithdrawRequest()
            {
                MaxWithdrawable = LightMoney.FromUnit(remaining, LightMoneyUnit.BTC),
                K1 = pullPaymentId,
                BalanceCheck = new Uri(Request.GetCurrentUrl()),
                CurrentBalance = LightMoney.FromUnit(remaining, LightMoneyUnit.BTC),
                MinWithdrawable =
                    LightMoney.FromUnit(
                        Math.Min(await _lightningLikePayoutHandler.GetMinimumPayoutAmount(pmi, null), remaining),
                        LightMoneyUnit.BTC),
                Tag = "withdrawRequest",
                Callback = new Uri(Request.GetCurrentUrl()),
            };
            if (pr is null)
            {
                return Ok(request);
            }

            if (!BOLT11PaymentRequest.TryParse(pr, out var result, network.NBitcoinNetwork) || result is null)
            {
                return BadRequest(new LNUrlStatusResponse {Status = "ERROR", Reason = "Pr was not a valid BOLT11"});
            }

            if (result.MinimumAmount < request.MinWithdrawable || result.MinimumAmount > request.MaxWithdrawable)
                return BadRequest(new LNUrlStatusResponse {Status = "ERROR", Reason = "Pr was not within bounds"});
            var store = await _storeRepository.FindStore(pp.StoreId);
            var pm = store!.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(method => method.PaymentId == pmi);
            if (pm is null)
            {
                return NotFound();
            }

            var claimResponse = await _pullPaymentHostedService.Claim(new ClaimRequest()
            {
                Destination = new BoltInvoiceClaimDestination(pr, result),
                PaymentMethodId = pmi,
                PullPaymentId = pullPaymentId,
                StoreId = pp.StoreId,
                Value = result.MinimumAmount.ToDecimal(LightMoneyUnit.BTC)
            });

            if (claimResponse.Result != ClaimRequest.ClaimResult.Ok)
                return BadRequest(new LNUrlStatusResponse {Status = "ERROR", Reason = "Pr could not be paid"});
            switch (claimResponse.PayoutData.State)
            {
                case PayoutState.AwaitingPayment:
                {
                    var client =
                        _lightningLikePaymentHandler.CreateLightningClient(pm, network);
                    PayResponse payResult;
                    try
                    {
                        payResult = await client.Pay(pr);
                    }
                    catch (Exception e)
                    {
                        payResult = new PayResponse(PayResult.Error, e.Message);
                    }

                    switch (payResult.Result)
                    {
                        case PayResult.Ok:
                            await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest()
                            {
                                PayoutId = claimResponse.PayoutData.Id, State = PayoutState.Completed
                            });

                            return Ok(new LNUrlStatusResponse {Status = "OK"});
                        default:
                            await _pullPaymentHostedService.Cancel(
                                new PullPaymentHostedService.CancelRequest(new string[]
                                {
                                    claimResponse.PayoutData.Id
                                }, null));

                            return Ok(new LNUrlStatusResponse
                            {
                                Status = "ERROR",
                                Reason = $"Pr could not be paid because {payResult.ErrorDetail}"
                            });
                    }
                }
                case PayoutState.AwaitingApproval:
                    return Ok(new LNUrlStatusResponse
                    {
                        Status = "OK",
                        Reason =
                            "The payment request has been recorded, but still needs to be approved before execution."
                    });
                case PayoutState.InProgress:
                case PayoutState.Completed:
                    return Ok(new LNUrlStatusResponse {Status = "OK"});
                case PayoutState.Cancelled:
                    return BadRequest(new LNUrlStatusResponse {Status = "ERROR", Reason = "Pr could not be paid"});
            }

            return Ok(request);
        }
        [HttpGet("pay/app/{appId}/{itemCode}")]
        public async Task<IActionResult> GetLNURLForApp(string cryptoCode, string appId, string itemCode = null)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null || !network.SupportLightning)
            {
                return NotFound();
            }

            var app = await _appService.GetApp(appId, null, true);
            if (app is null)
            {
                return NotFound();
            }

            var store = app.StoreData;
            if (store is null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(itemCode))
            {
                return NotFound();
            }

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var lnpmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var methods = store.GetSupportedPaymentMethods(_btcPayNetworkProvider);
            var lnUrlMethod =
                methods.FirstOrDefault(method => method.PaymentId == pmi) as LNURLPaySupportedPaymentMethod;
            var lnMethod = methods.FirstOrDefault(method => method.PaymentId == lnpmi);
            if (lnUrlMethod is null || lnMethod is null)
            {
                return NotFound();
            }

            ViewPointOfSaleViewModel.Item[] items = null;
            string currencyCode = null;
            switch (app.AppType)
            {
                case nameof(AppType.Crowdfund):
                    var cfS = app.GetSettings<CrowdfundSettings>();
                    currencyCode = cfS.TargetCurrency;
                    items = _appService.Parse(cfS.PerksTemplate, cfS.TargetCurrency);
                    break;
                case nameof(AppType.PointOfSale):
                    var posS = app.GetSettings<PointOfSaleSettings>();
                    currencyCode = posS.Currency;
                    items = _appService.Parse(posS.Template, posS.Currency);
                    break;
            }

            var item = items.FirstOrDefault(item1 =>
                item1.Id.Equals(itemCode, StringComparison.InvariantCultureIgnoreCase));
            if (item is null ||
                item.Inventory <= 0 ||
                (item.PaymentMethods?.Any() is true &&
                 item.PaymentMethods?.Any(s => PaymentMethodId.Parse(s) == pmi) is false))
            {
                return NotFound();
            }

            return await GetLNURL(cryptoCode, app.StoreDataId, currencyCode, null, null,
                () => (null, app, item, new List<string> {AppService.GetAppInternalTag(appId)}, item.Price.Value, true));
        }

        public class EditLightningAddressVM
        {
            public class EditLightningAddressItem : LightningAddressSettings.LightningAddressItem
            {
                [Required]
                [RegularExpression("[a-zA-Z0-9-_]+")]
                public string Username { get; set; }
            }

            public EditLightningAddressItem Add { get; set; }
            public List<EditLightningAddressItem> Items { get; set; } = new();
        }

        public class LightningAddressSettings
        {
            public class LightningAddressItem
            {
                public string StoreId { get; set; }
                [Display(Name = "Invoice currency")] public string CurrencyCode { get; set; }

                [Display(Name = "Min sats")]
                [Range(1, double.PositiveInfinity)]
                public decimal? Min { get; set; }

                [Display(Name = "Max sats")]
                [Range(1, double.PositiveInfinity)]
                public decimal? Max { get; set; }
            }

            public ConcurrentDictionary<string, LightningAddressItem> Items { get; set; } =
                new ConcurrentDictionary<string, LightningAddressItem>();

            public ConcurrentDictionary<string, string[]> StoreToItemMap { get; set; } =
                new ConcurrentDictionary<string, string[]>();

            public override string ToString()
            {
                return null;
            }
        }

        [HttpGet("~/.well-known/lnurlp/{username}")]
        public async Task<IActionResult> ResolveLightningAddress(string username)
        {
            var lightningAddressSettings = await _lightningAddressService.ResolveByAddress(username);
            if (lightningAddressSettings is null)
            {
                return NotFound("Unknown username");
            }

            var blob = lightningAddressSettings.Blob.GetBlob<LightningAddressDataBlob>();
            return await GetLNURL("BTC", lightningAddressSettings.StoreDataId, blob.CurrencyCode, blob.Min, blob.Max,
                () => (username, null, null, null, null, true));
        }

        [HttpGet("pay")]
        public async Task<IActionResult> GetLNURL(string cryptoCode, string storeId, string currencyCode = null,
            decimal? min = null, decimal? max = null,
            Func<(string username, AppData app, ViewPointOfSaleViewModel.Item item, List<string> additionalTags, decimal? invoiceAmount, bool? anyoneCanInvoice)>
                internalDetails = null)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null || !network.SupportLightning)
            {
                return NotFound("This network does not support Lightning");
            }

            var store = await _storeRepository.FindStore(storeId);
            if (store is null)
            {
                return NotFound("Store not found");
            }

            var storeBlob = store.GetStoreBlob();
            currencyCode ??= storeBlob.DefaultCurrency ?? cryptoCode;
            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var lnpmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var methods = store.GetSupportedPaymentMethods(_btcPayNetworkProvider);
            var lnUrlMethod =
                methods.FirstOrDefault(method => method.PaymentId == pmi) as LNURLPaySupportedPaymentMethod;
            var lnMethod = methods.FirstOrDefault(method => method.PaymentId == lnpmi);
            if (lnUrlMethod is null || lnMethod is null)
            {
                return NotFound("LNURL or Lightning payment method not found");
            }

            var blob = store.GetStoreBlob();
            if (blob.GetExcludedPaymentMethods().Match(pmi) || blob.GetExcludedPaymentMethods().Match(lnpmi))
            {
                return NotFound("LNURL or Lightning payment method disabled");
            }

            (string username, AppData app, ViewPointOfSaleViewModel.Item item, List<string> additionalTags, decimal? invoiceAmount, bool? anyoneCanInvoice) =
                (internalDetails ?? (() => (null, null, null, null, null, null)))();

            if ((anyoneCanInvoice ?? blob.AnyoneCanInvoice) is false)
            {
                return NotFound();
            }

            var lnAddress = username is null ? null : $"{username}@{Request.Host}";
            List<string[]> lnurlMetadata = new();

            var redirectUrl = app?.AppType switch
            {
                nameof(AppType.PointOfSale) => app.GetSettings<PointOfSaleSettings>().RedirectUrl ??
                                               HttpContext.Request.GetAbsoluteUri($"/apps/{app.Id}/pos"),
                _ => null
            };
            var invoiceRequest = new CreateInvoiceRequest
            {
                Amount = invoiceAmount,
                Checkout = new InvoiceDataBase.CheckoutOptions
                {
                    PaymentMethods = new[] { pmi.ToStringNormalized() },
                    Expiration = blob.InvoiceExpiration < TimeSpan.FromMinutes(2)
                        ? blob.InvoiceExpiration
                        : TimeSpan.FromMinutes(2),
                    RedirectURL = redirectUrl
                },
                Currency = currencyCode,
                Type = invoiceAmount is null ? InvoiceType.TopUp : InvoiceType.Standard,
            };

            if (item != null)
            {
                invoiceRequest.Metadata =
                    new InvoiceMetadata
                    {
                        ItemCode = item.Id, 
                        ItemDesc = item.Description, 
                        OrderId = AppService.GetAppOrderId(app)
                    }.ToJObject();
            }
            
            var i = await _invoiceController.CreateInvoiceCoreRaw(invoiceRequest, store, Request.GetAbsoluteRoot(), additionalTags);
            if (i.Type != InvoiceType.TopUp)
            {
                min = i.GetPaymentMethod(pmi).Calculate().Due.ToDecimal(MoneyUnit.Satoshi);
                max = item?.Price?.Type == ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Minimum ? null :  min;
            }

            if (!string.IsNullOrEmpty(username))
            {
                var pm = i.GetPaymentMethod(pmi);
                var paymentMethodDetails = (LNURLPayPaymentMethodDetails)pm.GetPaymentMethodDetails();
                paymentMethodDetails.ConsumedLightningAddress = lnAddress;
                pm.SetPaymentMethodDetails(paymentMethodDetails);
                await _invoiceRepository.UpdateInvoicePaymentMethod(i.Id, pm);
            }
            
            var description = blob.LightningDescriptionTemplate
                .Replace("{StoreName}", store.StoreName ?? "", StringComparison.OrdinalIgnoreCase)
                .Replace("{ItemDescription}", i.Metadata.ItemDesc ?? "", StringComparison.OrdinalIgnoreCase)
                .Replace("{OrderId}", i.Metadata.OrderId ?? "", StringComparison.OrdinalIgnoreCase);

            lnurlMetadata.Add(new[] {"text/plain", description});
            if (!string.IsNullOrEmpty(username))
            {
                lnurlMetadata.Add(new[] {"text/identifier", lnAddress});
            }
            return Ok(new LNURLPayRequest
            {
                Tag = "payRequest",
                MinSendable = new LightMoney(min ?? 1m, LightMoneyUnit.Satoshi),
                MaxSendable =
                    max is null
                        ? LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC)
                        : new LightMoney(max.Value, LightMoneyUnit.Satoshi),
                CommentAllowed = lnUrlMethod.LUD12Enabled ? 2000 : 0,
                Metadata = JsonConvert.SerializeObject(lnurlMetadata),
                Callback = new Uri(_linkGenerator.GetUriByAction(
                    action: nameof(GetLNURLForInvoice),
                    controller: "UILNURL",
                    values: new {cryptoCode, invoiceId = i.Id}, Request.Scheme, Request.Host, Request.PathBase))
            });
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

            if (comment is not null)
                comment = comment.Truncate(2000);

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var i = await _invoiceRepository.GetInvoice(invoiceId, true);
            
            var store = await _storeRepository.FindStore(i.StoreId);
            if (store is null)
            {
                return NotFound();
            }
            
            if (i.Status == InvoiceStatusLegacy.New)
            {
                var isTopup = i.IsUnsetTopUp();
                var lnurlSupportedPaymentMethod =
                    i.GetSupportedPaymentMethod<LNURLPaySupportedPaymentMethod>(pmi).FirstOrDefault();
                if (lnurlSupportedPaymentMethod is null)
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

                List<string[]> lnurlMetadata = new();

                var blob = store.GetStoreBlob();
                var description = blob.LightningDescriptionTemplate
                    .Replace("{StoreName}", store.StoreName ?? "", StringComparison.OrdinalIgnoreCase)
                    .Replace("{ItemDescription}", i.Metadata.ItemDesc ?? "", StringComparison.OrdinalIgnoreCase)
                    .Replace("{OrderId}", i.Metadata.OrderId ?? "", StringComparison.OrdinalIgnoreCase);

                lnurlMetadata.Add(new[] {"text/plain", description});
                if (!string.IsNullOrEmpty(paymentMethodDetails.ConsumedLightningAddress))
                {
                    lnurlMetadata.Add(new[] {"text/identifier", paymentMethodDetails.ConsumedLightningAddress});
                }

                var metadata = JsonConvert.SerializeObject(lnurlMetadata);
                if (amount.HasValue && (amount < min || amount > max))
                {
                    return BadRequest(new LNUrlStatusResponse {Status = "ERROR", Reason = "Amount is out of bounds."});
                }

                LNURLPayRequest.LNURLPayRequestCallbackResponse.ILNURLPayRequestSuccessAction successAction = null;

                if ((i.ReceiptOptions?.Enabled ??blob.ReceiptOptions.Enabled ) is true)
                {
                    successAction =
                        new LNURLPayRequest.LNURLPayRequestCallbackResponse.LNURLPayRequestSuccessActionUrl()
                        {
                            Tag = "url",
                            Description = "Thank you for your purchase. Here is your receipt",
                            Url = _linkGenerator.GetUriByAction(HttpContext, "InvoiceReceipt", "UIInvoice", new { invoiceId})
                        };
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
                
                if (string.IsNullOrEmpty(paymentMethodDetails.BOLT11) || paymentMethodDetails.GeneratedBoltAmount != amount)
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

                    var descriptionHash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(metadata)), false);
                    LightningInvoice invoice;
                    try
                    {
                        var expiry = i.ExpirationTime.ToUniversalTime() - DateTimeOffset.UtcNow;
                        var param = new CreateInvoiceParams(amount.Value, descriptionHash, expiry)
                        {
                            PrivateRouteHints = blob.LightningPrivateRouteHints
                        };
                        invoice = await client.CreateInvoice(param);
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
                        Disposable = true, Routes = Array.Empty<string>(), Pr = paymentMethodDetails.BOLT11,
                        SuccessAction = successAction
                    });
                }

                if (paymentMethodDetails.GeneratedBoltAmount == amount)
                {
                    if (lnurlSupportedPaymentMethod.LUD12Enabled && paymentMethodDetails.ProvidedComment != comment)
                    {
                        paymentMethodDetails.ProvidedComment = comment;
                        lightningPaymentMethod.SetPaymentMethodDetails(paymentMethodDetails);
                        await _invoiceRepository.UpdateInvoicePaymentMethod(invoiceId, lightningPaymentMethod);
                    }

                    return Ok(new LNURLPayRequest.LNURLPayRequestCallbackResponse
                    {
                        Disposable = true, Routes = Array.Empty<string>(), Pr = paymentMethodDetails.BOLT11,
                        SuccessAction = successAction
                    });
                }
            }

            return BadRequest(new LNUrlStatusResponse
            {
                Status = "ERROR", Reason = "Invoice not in a valid payable state"
            });
        }


        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("~/stores/{storeId}/plugins/lightning-address")]
        public async Task<IActionResult> EditLightningAddress(string storeId)
        {
            if (ControllerContext.HttpContext.GetStoreData().GetEnabledPaymentIds(_btcPayNetworkProvider)
                .All(id => id.PaymentType != LNURLPayPaymentType.Instance))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "LNURL is required for lightning addresses but has not yet been enabled.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.GeneralSettings), "UIStores", new {storeId});
            }

            var addresses =
                await _lightningAddressService.Get(new LightningAddressQuery() {StoreIds = new[] {storeId}});

            return View(new EditLightningAddressVM
            {
                Items = addresses.Select(s =>
                    {
                        var blob = s.Blob.GetBlob<LightningAddressDataBlob>();
                        return new EditLightningAddressVM.EditLightningAddressItem
                        {
                            Max = blob.Max,
                            Min = blob.Min,
                            CurrencyCode = blob.CurrencyCode,
                            StoreId = storeId,
                            Username = s.Username,
                        };
                    }
                ).ToList()
            });
        }


        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("~/stores/{storeId}/plugins/lightning-address")]
        public async Task<IActionResult> EditLightningAddress(string storeId, [FromForm] EditLightningAddressVM vm,
            string command, [FromServices] CurrencyNameTable currencyNameTable)
        {
            if (command == "add")
            {
                if (!string.IsNullOrEmpty(vm.Add.CurrencyCode) &&
                    currencyNameTable.GetCurrencyData(vm.Add.CurrencyCode, false) is null)
                {
                    vm.AddModelError(addressVm => addressVm.Add.CurrencyCode, "Currency is invalid", this);
                }

                if (!ModelState.IsValid)
                {
                    return View(vm);
                }
               

                if (await _lightningAddressService.Set(new LightningAddressData()
                    {
                        StoreDataId = storeId,
                        Username = vm.Add.Username,
                        Blob = new LightningAddressDataBlob()
                        {
                            Max = vm.Add.Max, Min = vm.Add.Min, CurrencyCode = vm.Add.CurrencyCode
                        }.SerializeBlob()
                    }))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = "Lightning address added successfully."
                    });
                }
                else
                {
                    vm.AddModelError(addressVm => addressVm.Add.Username, "Username is already taken", this);
                    
                    if (!ModelState.IsValid)
                    {
                        return View(vm);
                    }
                }
                return RedirectToAction("EditLightningAddress");
            }

            if (command.StartsWith("remove", StringComparison.InvariantCultureIgnoreCase))
            {
                var index = command.Substring(command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1);
                if (await _lightningAddressService.Remove(index, storeId))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = $"Lightning address {index} removed successfully."
                    });
                    return RedirectToAction("EditLightningAddress");
                }
                else
                {
                    vm.AddModelError(addressVm => addressVm.Add.Username, "Username could not be removed", this);
                    
                    if (!ModelState.IsValid)
                    {
                        return View(vm);
                    }
                }
            }
            
            return View(vm);

        }
    }
}
