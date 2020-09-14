#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Common.Altcoins.Fiat;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = BTCPayServer.Services.Invoices.PaymentMethod;

namespace BTCPayServer.Services.Altcoins.Stripe.Payments
{
    public class StripePaymentMethodHandler : PaymentMethodHandlerBase<StripeSupportedPaymentMethod, FiatPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly LinkGenerator _linkGenerator;

        public StripePaymentMethodHandler(BTCPayNetworkProvider networkProvider, IHttpContextAccessor httpContextAccessor, LinkGenerator linkGenerator)
        {
            _networkProvider = networkProvider;
            _httpContextAccessor = httpContextAccessor;
            _linkGenerator = linkGenerator;
        }
        public override PaymentType PaymentType => StripePaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs, StripeSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, FiatPayNetwork network, object preparePaymentObject)
        {
            long amt;
            try
            {
                amt =   MoneyExtensions.Convert(paymentMethod.Calculate().Due.ToDecimal(MoneyUnit.BTC), network.Divisibility);
            }
            catch (Exception)
            {
                amt = MoneyExtensions.Convert((paymentMethod.ParentEntity.Price / paymentMethod.Rate), network.Divisibility);
            }

            if (supportedPaymentMethod.UseCheckout)
            {
                var service = new SessionService(new StripeClient(supportedPaymentMethod.SecretKey));
            
                Session session = await service.CreateAsync(new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> {"card",},
                    Metadata = new Dictionary<string, string>()
                    {
                        {"invoice", paymentMethod.ParentEntity.Id}
                    },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                ProductData = new SessionLineItemPriceDataProductDataOptions()
                                {
                                    Name = "BTCPay Invoice"
                                },
                                UnitAmount = amt,
                                Currency = supportedPaymentMethod.CryptoCode,
                            },
                            Quantity = 1,
                        },
                    },
                    SuccessUrl = _linkGenerator.GetUriByAction(nameof(InvoiceController.Checkout), "Invoice", new
                    {
                        invoiceId = paymentMethod.ParentEntity.Id,
                    },_httpContextAccessor.HttpContext.Request.Scheme,_httpContextAccessor.HttpContext.Request.Host,_httpContextAccessor.HttpContext.Request.PathBase),
                    CancelUrl = _linkGenerator.GetUriByAction(nameof(InvoiceController.Checkout), "Invoice", new
                    {
                        invoiceId = paymentMethod.ParentEntity.Id,
                    },_httpContextAccessor.HttpContext.Request.Scheme,_httpContextAccessor.HttpContext.Request.Host,_httpContextAccessor.HttpContext.Request.PathBase),
                    Mode = "payment"
                });
                
                return new StripePaymentMethodDetails()
                {
                    PublishableKey = supportedPaymentMethod.PublishableKey,
                    SessionId = session.Id,
                    Amount = amt
                };
            }
            else
            {
                var piService = new PaymentIntentService(new StripeClient(supportedPaymentMethod.SecretKey));

                var paymentIntent = await piService.CreateAsync(new PaymentIntentCreateOptions()
                {
                    Amount = amt, Currency = supportedPaymentMethod.CryptoCode,Metadata = new Dictionary<string, string>
                    {
                        {"invoice", paymentMethod.ParentEntity.Id}
                    },
                });
                
                return new StripePaymentMethodDetails()
                {
                    PaymentIntentId =  paymentIntent.Id,
                    PaymentIntentClientSecret =  paymentIntent.ClientSecret,
                    PublishableKey = supportedPaymentMethod.PublishableKey,
                    Amount = amt
                };
            }
        }

        public override object PreparePayment(StripeSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return new Prepare();
        }

        class Prepare
        {
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse, StoreBlob storeBlob)
        {
            
            var network = _networkProvider.GetNetwork<FiatPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName();
            model.CryptoImage = GetCryptoImage();
            model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrl;
            
        }
        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            return GetCryptoImage();
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            return GetPaymentMethodName();
        }

        public override Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob, Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount,
            PaymentMethodId paymentMethodId)
        {
            return Task.FromResult<string>(null);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll()
                .Where(network => network is FiatPayNetwork)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Stripe/StripeMethodCheckout",
                CheckoutBodyVueComponentName = "StripeMethodCheckout",
                CheckoutHeaderVueComponentName = "",
                NoScriptPartialName = "Stripe/StripeMethodCheckoutNoScript"
            };
        }

        private string GetCryptoImage()
        {
            return "/imlegacy/stripe.jpg";
        }

        private string GetPaymentMethodName()
        {
            return $"Stripe";
        }
    }
}
#endif
