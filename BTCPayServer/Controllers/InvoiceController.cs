using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController : Controller
    {
        InvoiceRepository _InvoiceRepository;
        ContentSecurityPolicies _CSP;
        RateFetcher _RateProvider;
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
            RateFetcher rateProvider,
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
            if (!store.HasClaim(Policies.CanCreateInvoice.Key))
                throw new UnauthorizedAccessException();
            InvoiceLogs logs = new InvoiceLogs();
            logs.Write("Creation of invoice starting");
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
            entity.NotificationEmail = invoice.NotificationEmail;
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
            var excludeFilter = storeBlob.GetExcludedPaymentMethods(); // Here we can compose filters from other origin with PaymentFilter.Any()
            foreach (var network in store.GetSupportedPaymentMethods(_NetworkProvider)
                                                .Where(s => !excludeFilter.Match(s.PaymentId))
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

            var fetchingAll = WhenAllFetched(logs, fetchingByCurrencyPair);
            var supportedPaymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider)
                                               .Where(s => !excludeFilter.Match(s.PaymentId))
                                               .Select(c =>
                                                (Handler: (IPaymentMethodHandler)_ServiceProvider.GetService(typeof(IPaymentMethodHandler<>).MakeGenericType(c.GetType())),
                                                SupportedPaymentMethod: c,
                                                Network: _NetworkProvider.GetNetwork(c.PaymentId.CryptoCode)))
                                                .Where(c => c.Network != null)
                                                .Select(o =>
                                                    (SupportedPaymentMethod: o.SupportedPaymentMethod,
                                                    PaymentMethod: CreatePaymentMethodAsync(fetchingByCurrencyPair, o.Handler, o.SupportedPaymentMethod, o.Network, entity, store, logs)))
                                                .ToList();
            List<ISupportedPaymentMethod> supported = new List<ISupportedPaymentMethod>();
            var paymentMethods = new PaymentMethodDictionary();
            foreach (var o in supportedPaymentMethods)
            {
                var paymentMethod = await o.PaymentMethod;
                if (paymentMethod == null)
                    continue;
                supported.Add(o.SupportedPaymentMethod);
                paymentMethods.Add(paymentMethod);
            }

            if (supported.Count == 0)
            {
                StringBuilder errors = new StringBuilder();
                errors.AppendLine("No payment method available for this store");
                foreach (var error in logs.ToList())
                {
                    errors.AppendLine(error.ToString());
                }
                throw new BitpayHttpException(400, errors.ToString());
            }

            entity.SetSupportedPaymentMethods(supported);
            entity.SetPaymentMethods(paymentMethods);
            entity.PosData = invoice.PosData;
            entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity, logs, _NetworkProvider);
            await fetchingAll;
            _EventAggregator.Publish(new Events.InvoiceEvent(entity.EntityToDTO(_NetworkProvider), 1001, "invoice_created"));
            var resp = entity.EntityToDTO(_NetworkProvider);
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        private Task WhenAllFetched(InvoiceLogs logs, Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair)
        {
            return Task.WhenAll(fetchingByCurrencyPair.Select(async pair =>
            {
                var rateResult = await pair.Value;
                logs.Write($"{pair.Key}: The rating rule is {rateResult.Rule}");
                logs.Write($"{pair.Key}: The evaluated rating rule is {rateResult.EvaluatedRule}");
                if (rateResult.Errors.Count != 0)
                {
                    var allRateRuleErrors = string.Join(", ", rateResult.Errors.ToArray());
                    logs.Write($"{pair.Key}: Rate rule error ({allRateRuleErrors})");
                }
                foreach (var ex in rateResult.ExchangeExceptions)
                {
                    logs.Write($"{pair.Key}: Exception reaching exchange {ex.ExchangeName} ({ex.Exception.Message})");
                }
            }).ToArray());
        }

        private async Task<PaymentMethod> CreatePaymentMethodAsync(Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair, IPaymentMethodHandler handler, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network, InvoiceEntity entity, StoreData store, InvoiceLogs logs)
        {
            try
            {
                var storeBlob = store.GetStoreBlob();
                var preparePayment = handler.PreparePayment(supportedPaymentMethod, store, network);
                var rate = await fetchingByCurrencyPair[new CurrencyPair(network.CryptoCode, entity.ProductInformation.Currency)];
                if (rate.BidAsk == null)
                {
                    return null;
                }
                PaymentMethod paymentMethod = new PaymentMethod();
                paymentMethod.ParentEntity = entity;
                paymentMethod.Network = network;
                paymentMethod.SetId(supportedPaymentMethod.PaymentId);
                paymentMethod.Rate = rate.BidAsk.Bid;
                var paymentDetails = await handler.CreatePaymentMethodDetails(supportedPaymentMethod, paymentMethod, store, network, preparePayment);
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
                    if (limitValueRate.BidAsk != null)
                    {
                        var limitValueCrypto = Money.Coins(limitValue.Value / limitValueRate.BidAsk.Bid);
                        if (compare(paymentMethod.Calculate().Due, limitValueCrypto))
                        {
                            logs.Write($"{supportedPaymentMethod.PaymentId.CryptoCode}: {errorMessage}");
                            return null;
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
            catch (PaymentMethodUnavailableException ex)
            {
                logs.Write($"{supportedPaymentMethod.PaymentId.CryptoCode}: Payment method unavailable ({ex.Message})");
            }
            catch (Exception ex)
            {
                logs.Write($"{supportedPaymentMethod.PaymentId.CryptoCode}: Unexpected exception ({ex.ToString()})");
            }
            return null;
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
