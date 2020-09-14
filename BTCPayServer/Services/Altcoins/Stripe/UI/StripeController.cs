#if ALTCOINS
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Common.Altcoins.Fiat;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Stripe.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Stripe;
using Stripe.Checkout;

namespace BTCPayServer.Services.Altcoins.Stripe.UI
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class StripeController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly LinkGenerator _linkGenerator;
        private readonly EventAggregator _eventAggregator;

        public StripeController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider, InvoiceRepository invoiceRepository, LinkGenerator linkGenerator, EventAggregator eventAggregator)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _linkGenerator = linkGenerator;
            _eventAggregator = eventAggregator;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpPost("stores/{storeId}/stripe/webhook")]
        public async Task<IActionResult> HandleStripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ParseEvent(json);
                string invoiceId = null;
                // Handle the event
                if (stripeEvent.Type == global::Stripe.Events.PaymentIntentSucceeded && stripeEvent.Data.Object is PaymentIntent paymentIntent && paymentIntent.Metadata.TryGetValue("invoice", out invoiceId))
                {
                    var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(paymentIntent.Currency);

                    
                    _eventAggregator.Publish(new StripeService.HandleStripeWebhookPaymentData()
                    {
                        InvoiceId = invoiceId,
                        PaymentMethodId = new PaymentMethodId(paymentIntent.Currency, StripePaymentType.Instance),
                        PaymentData = new StripePaymentData()
                        {
                            Network = network,
                            Amount = paymentIntent.AmountReceived,
                            PaymentIntentId = paymentIntent.Id,
                            SessionId = null,
                        
                        }
                        
                    });

                }else if (stripeEvent.Type == global::Stripe.Events.CheckoutSessionCompleted &&
                          stripeEvent.Data.Object is Session session &&
                          session.Metadata.TryGetValue("invoice", out invoiceId))
                {
                    
                    var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(session.Currency);
                    _eventAggregator.Publish(new StripeService.HandleStripeWebhookPaymentData()
                    {
                        InvoiceId = invoiceId,
                        PaymentMethodId = new PaymentMethodId(session.Currency, StripePaymentType.Instance),
                        PaymentData = new StripePaymentData()
                        {
                            Network = network,
                            Amount = session.AmountSubtotal??session.PaymentIntent.AmountReceived,
                            SessionId = session.Id,
                            CryptoCode = network.CryptoCode
                        }
                        
                    });
                }

                return BadRequest();
            }
            catch (StripeException)
            {
                return BadRequest();
            }
        }
        

        [HttpGet("stores/{storeId}/stripe")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult GetStoreStripePaymentMethods()
        {
            var stripe = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<StripeSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();

            return View(new StripePaymentMethodListViewModel()
            {
                Items = _btcPayNetworkProvider.GetAll().OfType<FiatPayNetwork>().Select(network => GetStripePaymentMethodViewModel(stripe, network.CryptoCode, excludeFilters))
            });
        }

        private StripePaymentMethodViewModel GetStripePaymentMethodViewModel(
            IEnumerable<StripeSupportedPaymentMethod> stripe, string cryptoCode,
            IPaymentFilter excludeFilters)
        {
            var settings = stripe.SingleOrDefault(method => method.CryptoCode == cryptoCode);
            return new StripePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(new PaymentMethodId(cryptoCode, StripePaymentType.Instance)),
                CryptoCode = cryptoCode,
                SecretKey = settings?.SecretKey,
                PublishableKey = settings?.PublishableKey,
                UseCheckout = settings?.UseCheckout is true
            };
        }

        [HttpGet("stores/{storeId}/stripe/{cryptoCode}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult GetStoreStripePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }
            var vm = GetStripePaymentMethodViewModel(StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                    .OfType<StripeSupportedPaymentMethod>(), cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods());
            return View(nameof(GetStoreStripePaymentMethod), vm);
        }

        [HttpPost("stores/{storeId}/stripe/{cryptoCode}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> GetStoreStripePaymentMethod(StripePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {

                var vm = GetStripePaymentMethodViewModel(StoreData
                        .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                        .OfType<StripeSupportedPaymentMethod>(), cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods());

                vm.Enabled = viewModel.Enabled;
                return View(vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetSupportedPaymentMethod(new StripeSupportedPaymentMethod()
            {
                PublishableKey = viewModel.PublishableKey,
                SecretKey = viewModel.SecretKey,
                CryptoCode = viewModel.CryptoCode,
                UseCheckout = viewModel.UseCheckout
            });

            blob.SetExcluded(new PaymentMethodId(viewModel.CryptoCode, MoneroPaymentType.Instance), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(storeData);

            string webhookUrl = _linkGenerator.GetUriByAction("HandleStripeWebhook", "Stripe",
                new {storeId = storeData.Id},
                Request.Scheme, Request.Host, Request.PathBase);
            var client = new StripeClient(viewModel.SecretKey);
            var webhookService = new WebhookEndpointService(client);
            var webhookCreatedAlready = false;
            try
            {
                var existing = await webhookService.ListAsync();
                webhookCreatedAlready = existing.Data.Any(endpoint => endpoint.Url == webhookUrl);
                
            }
            catch (Exception)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"{cryptoCode} settings updated but unsure if keys are correct",
                    Severity = StatusMessageModel.StatusSeverity.Warning
                });
                return View(viewModel);
            }
            
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"{cryptoCode} settings updated",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            if (!webhookCreatedAlready)
            {

                try
                {
                    await webhookService.CreateAsync(new WebhookEndpointCreateOptions()
                    {
                        Description = "BTCPay webhook",
                        EnabledEvents =
                            new List<string>()
                            {
                                global::Stripe.Events.PaymentIntentSucceeded,
                                global::Stripe.Events.CheckoutSessionCompleted
                            },
                        Url = webhookUrl
                    });
                }
                catch (Exception)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Settings updated but cannot set webhook in stripe for fast payment detection.",
                        Severity = StatusMessageModel.StatusSeverity.Warning
                    });

                    return View(viewModel);
                }

            }

            return RedirectToAction("GetStoreStripePaymentMethods",
                new { storeId = StoreData.Id });
        }

        public class StripePaymentMethodListViewModel
        {
            public IEnumerable<StripePaymentMethodViewModel> Items { get; set; }
        }

        public class StripePaymentMethodViewModel
        {
            public string CryptoCode { get; set; }
            public bool Enabled { get; set; }
            
            [DisplayName("Private/Secret Key")]
            public string SecretKey { get; set; }
            
            [DisplayName("Public/Publishable Key")]
            public string PublishableKey { get; set; }
          
            [DisplayName("Use Checkout redirect instead of embedded")]
            public bool UseCheckout { get; set; }
        }
    }
}
#endif
