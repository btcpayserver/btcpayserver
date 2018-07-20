using BTCPayServer.Authentication;
using System.Reflection;
using System.Linq;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Newtonsoft.Json;
using System.Globalization;
using NBitcoin;
using NBitcoin.DataEncoders;
using BTCPayServer.Filters;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin.Payment;
using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using System.Security.Claims;
using BTCPayServer.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Validations;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Routing;
using NBXplorer.DerivationStrategy;
using NBXplorer;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController : Controller
    {
        InvoiceRepository _InvoiceRepository;
        ContentSecurityPolicies _CSP;
        BTCPayRateProviderFactory _RateProvider;
        StoreRepository _StoreRepository;
        UserManager<ApplicationUser> _UserManager;
        private CurrencyNameTable _CurrencyNameTable;
        EventAggregator _EventAggregator;
        BTCPayNetworkProvider _NetworkProvider;
        private readonly BTCPayWalletProvider _WalletProvider;
        IServiceProvider _ServiceProvider;
        public InvoiceController(
            IServiceProvider serviceProvider,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            BTCPayRateProviderFactory rateProvider,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            BTCPayWalletProvider walletProvider,
            ContentSecurityPolicies csp,
            BTCPayNetworkProvider networkProvider)
        {
            _ServiceProvider = serviceProvider;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
            _UserManager = userManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _WalletProvider = walletProvider;
            _CSP = csp;
        }


        internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(Invoice invoice, StoreData store, string serverUrl)
        {
            var entity = new InvoiceEntity
            {
                InvoiceTime = DateTimeOffset.UtcNow
            };

            var storeBlob = store.GetStoreBlob();
            Uri notificationUri = Uri.IsWellFormedUriString(invoice.NotificationURL, UriKind.Absolute) ? new Uri(invoice.NotificationURL, UriKind.Absolute) : null;
            if (notificationUri == null || (notificationUri.Scheme != "http" && notificationUri.Scheme != "https")) //TODO: Filer non routable addresses ?
                notificationUri = null;
            EmailAddressAttribute emailValidator = new EmailAddressAttribute();
            entity.ExpirationTime = entity.InvoiceTime.AddMinutes(storeBlob.InvoiceExpiration);
            entity.MonitoringExpiration = entity.ExpirationTime + TimeSpan.FromMinutes(storeBlob.MonitoringExpiration);
            entity.OrderId = invoice.OrderId;
            entity.ServerUrl = serverUrl;
            entity.FullNotifications = invoice.FullNotifications || invoice.ExtendedNotifications;
            entity.ExtendedNotifications = invoice.ExtendedNotifications;
            entity.NotificationURL = notificationUri?.AbsoluteUri;
            entity.BuyerInformation = Map<Invoice, BuyerInformation>(invoice);
            entity.PaymentTolerance = storeBlob.PaymentTolerance;
            //Another way of passing buyer info to support
            FillBuyerInfo(invoice.Buyer, entity.BuyerInformation);
            if (entity?.BuyerInformation?.BuyerEmail != null)
            {
                if (!EmailValidator.IsEmail(entity.BuyerInformation.BuyerEmail))
                    throw new BitpayHttpException(400, "Invalid email");
                entity.RefundMail = entity.BuyerInformation.BuyerEmail;
            }
            entity.ProductInformation = Map<Invoice, ProductInformation>(invoice);
            entity.RedirectURL = invoice.RedirectURL ?? store.StoreWebsite;
            if (!Uri.IsWellFormedUriString(entity.RedirectURL, UriKind.Absolute))
                entity.RedirectURL = null;

            entity.Status = "new";
            entity.SpeedPolicy = ParseSpeedPolicy(invoice.TransactionSpeed, store.SpeedPolicy);

            HashSet<CurrencyPair> currencyPairsToFetch = new HashSet<CurrencyPair>();
            var rules = storeBlob.GetRateRules(_NetworkProvider);

            foreach (var network in store.GetSupportedPaymentMethods(_NetworkProvider)
                                               .Select(c => _NetworkProvider.GetNetwork(c.PaymentId.CryptoCode))
                                                .Where(c => c != null))
            {
                currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, invoice.Currency));
                if (storeBlob.LightningMaxValue != null)
                    currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, storeBlob.LightningMaxValue.Currency));
                if (storeBlob.OnChainMinValue != null)
                    currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, storeBlob.OnChainMinValue.Currency));
            }

            var rateRules = storeBlob.GetRateRules(_NetworkProvider);
            var fetchingByCurrencyPair = _RateProvider.FetchRates(currencyPairsToFetch, rateRules);

            var supportedPaymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider)
                                               .Select(c =>
                                                (Handler: (IPaymentMethodHandler)_ServiceProvider.GetService(typeof(IPaymentMethodHandler<>).MakeGenericType(c.GetType())),
                                                SupportedPaymentMethod: c,
                                                Network: _NetworkProvider.GetNetwork(c.PaymentId.CryptoCode)))
                                                .Where(c => c.Network != null)
                                                .Select(o =>
                                                    (SupportedPaymentMethod: o.SupportedPaymentMethod,
                                                    PaymentMethod: CreatePaymentMethodAsync(fetchingByCurrencyPair, o.Handler, o.SupportedPaymentMethod, o.Network, entity, store)))
                                                .ToList();

            List<string> invoiceLogs = new List<string>();
            List<ISupportedPaymentMethod> supported = new List<ISupportedPaymentMethod>();
            var paymentMethods = new PaymentMethodDictionary();

            foreach (var pair in fetchingByCurrencyPair)
            {
                var rateResult = await pair.Value;
                invoiceLogs.Add($"{pair.Key}: The rating rule is {rateResult.Rule}");
                invoiceLogs.Add($"{pair.Key}: The evaluated rating rule is {rateResult.EvaluatedRule}");
                if (rateResult.Errors.Count != 0)
                {
                    var allRateRuleErrors = string.Join(", ", rateResult.Errors.ToArray());
                    invoiceLogs.Add($"{pair.Key}: Rate rule error ({allRateRuleErrors})");
                }
                if (rateResult.ExchangeExceptions.Count != 0)
                {
                    foreach (var ex in rateResult.ExchangeExceptions)
                    {
                        invoiceLogs.Add($"{pair.Key}: Exception reaching exchange {ex.ExchangeName} ({ex.Exception.Message})");
                    }
                }
            }

            foreach (var o in supportedPaymentMethods)
            {
                try
                {
                    var paymentMethod = await o.PaymentMethod;
                    if (paymentMethod == null)
                        throw new PaymentMethodUnavailableException("Payment method unavailable");
                    supported.Add(o.SupportedPaymentMethod);
                    paymentMethods.Add(paymentMethod);
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    invoiceLogs.Add($"{o.SupportedPaymentMethod.PaymentId.CryptoCode} ({o.SupportedPaymentMethod.PaymentId.PaymentType}): Payment method unavailable ({ex.Message})");
                }
                catch (Exception ex)
                {
                    invoiceLogs.Add($"{o.SupportedPaymentMethod.PaymentId.CryptoCode} ({o.SupportedPaymentMethod.PaymentId.PaymentType}): Unexpected exception ({ex.ToString()})");
                }
            }

            if (supported.Count == 0)
            {
                StringBuilder errors = new StringBuilder();
                errors.AppendLine("No payment method available for this store");
                foreach (var error in invoiceLogs)
                {
                    errors.AppendLine(error);
                }
                throw new BitpayHttpException(400, errors.ToString());
            }

            entity.SetSupportedPaymentMethods(supported);
            entity.SetPaymentMethods(paymentMethods);
            entity.PosData = invoice.PosData;
            entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity, invoiceLogs, _NetworkProvider);

            _EventAggregator.Publish(new Events.InvoiceEvent(entity.EntityToDTO(_NetworkProvider), 1001, "invoice_created"));
            var resp = entity.EntityToDTO(_NetworkProvider);
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        private async Task<PaymentMethod> CreatePaymentMethodAsync(Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair, IPaymentMethodHandler handler, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network, InvoiceEntity entity, StoreData store)
        {
            var storeBlob = store.GetStoreBlob();
            var rate = await fetchingByCurrencyPair[new CurrencyPair(network.CryptoCode, entity.ProductInformation.Currency)];
            if (rate.Value == null)
                return null;
            PaymentMethod paymentMethod = new PaymentMethod();
            paymentMethod.ParentEntity = entity;
            paymentMethod.Network = network;
            paymentMethod.SetId(supportedPaymentMethod.PaymentId);
            paymentMethod.Rate = rate.Value.Value;
            var paymentDetails = await handler.CreatePaymentMethodDetails(supportedPaymentMethod, paymentMethod, store, network);
            if (storeBlob.NetworkFeeDisabled)
                paymentDetails.SetNoTxFee();
            paymentMethod.SetPaymentMethodDetails(paymentDetails);

            Func<Money, Money, bool> compare = null;
            CurrencyValue limitValue = null;
            string errorMessage = null;
            if (supportedPaymentMethod.PaymentId.PaymentType == PaymentTypes.LightningLike &&
               storeBlob.LightningMaxValue != null)
            {
                compare = (a, b) => a > b;
                limitValue = storeBlob.LightningMaxValue;
                errorMessage = "The amount of the invoice is too high to be paid with lightning";
            }
            else if (supportedPaymentMethod.PaymentId.PaymentType == PaymentTypes.BTCLike &&
               storeBlob.OnChainMinValue != null)
            {
                compare = (a, b) => a < b;
                limitValue = storeBlob.OnChainMinValue;
                errorMessage = "The amount of the invoice is too low to be paid on chain";
            }

            if (compare != null)
            {
                var limitValueRate = await fetchingByCurrencyPair[new CurrencyPair(network.CryptoCode, limitValue.Currency)];
                if (limitValueRate.Value.HasValue)
                {
                    var limitValueCrypto = Money.Coins(limitValue.Value / limitValueRate.Value.Value);
                    if (compare(paymentMethod.Calculate().Due, limitValueCrypto))
                    {
                        throw new PaymentMethodUnavailableException(errorMessage);
                    }
                }
            }
            ///////////////


#pragma warning disable CS0618
            if (paymentMethod.GetId().IsBTCOnChain)
            {
                entity.TxFee = paymentMethod.TxFee;
                entity.Rate = paymentMethod.Rate;
                entity.DepositAddress = paymentMethod.DepositAddress;
            }
#pragma warning restore CS0618
            return paymentMethod;
        }

        private SpeedPolicy ParseSpeedPolicy(string transactionSpeed, SpeedPolicy defaultPolicy)
        {
            if (transactionSpeed == null)
                return defaultPolicy;
            var mappings = new Dictionary<string, SpeedPolicy>();
            mappings.Add("low", SpeedPolicy.LowSpeed);
            mappings.Add("low-medium", SpeedPolicy.LowMediumSpeed);
            mappings.Add("medium", SpeedPolicy.MediumSpeed);
            mappings.Add("high", SpeedPolicy.HighSpeed);
            if (!mappings.TryGetValue(transactionSpeed, out SpeedPolicy policy))
                policy = defaultPolicy;
            return policy;
        }

        private void FillBuyerInfo(Buyer buyer, BuyerInformation buyerInformation)
        {
            if (buyer == null)
                return;
            buyerInformation.BuyerAddress1 = buyerInformation.BuyerAddress1 ?? buyer.Address1;
            buyerInformation.BuyerAddress2 = buyerInformation.BuyerAddress2 ?? buyer.Address2;
            buyerInformation.BuyerCity = buyerInformation.BuyerCity ?? buyer.City;
            buyerInformation.BuyerCountry = buyerInformation.BuyerCountry ?? buyer.country;
            buyerInformation.BuyerEmail = buyerInformation.BuyerEmail ?? buyer.email;
            buyerInformation.BuyerName = buyerInformation.BuyerName ?? buyer.Name;
            buyerInformation.BuyerPhone = buyerInformation.BuyerPhone ?? buyer.phone;
            buyerInformation.BuyerState = buyerInformation.BuyerState ?? buyer.State;
            buyerInformation.BuyerZip = buyerInformation.BuyerZip ?? buyer.zip;
        }

        private TDest Map<TFrom, TDest>(TFrom data)
        {
            return JsonConvert.DeserializeObject<TDest>(JsonConvert.SerializeObject(data));
        }
    }
}
