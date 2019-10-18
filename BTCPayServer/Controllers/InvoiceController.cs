using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
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
    [Filters.BitpayAPIConstraint(false)]
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
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        IServiceProvider _ServiceProvider;
        public InvoiceController(
            IServiceProvider serviceProvider,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            RateFetcher rateProvider,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            ContentSecurityPolicies csp,
            BTCPayNetworkProvider networkProvider,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary)
        {
            _ServiceProvider = serviceProvider;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
            _UserManager = userManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _CSP = csp;
        }


        internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(CreateInvoiceRequest invoice, StoreData store, string serverUrl, List<string> additionalTags = null, CancellationToken cancellationToken = default)
        {
            invoice.Currency = invoice.Currency?.ToUpperInvariant() ?? "USD";
            InvoiceLogs logs = new InvoiceLogs();
            logs.Write("Creation of invoice starting");
            var entity = _InvoiceRepository.CreateNewInvoice();

            var getAppsTaggingStore = _InvoiceRepository.GetAppsTaggingStore(store.Id);
            var storeBlob = store.GetStoreBlob();
            EmailAddressAttribute emailValidator = new EmailAddressAttribute();
            entity.ExpirationTime = entity.InvoiceTime.AddMinutes(storeBlob.InvoiceExpiration);
            entity.MonitoringExpiration = entity.ExpirationTime + TimeSpan.FromMinutes(storeBlob.MonitoringExpiration);
            entity.OrderId = invoice.OrderId;
            entity.ServerUrl = serverUrl;
            entity.FullNotifications = invoice.FullNotifications || invoice.ExtendedNotifications;
            entity.ExtendedNotifications = invoice.ExtendedNotifications;
            entity.NotificationURLTemplate = invoice.NotificationURL;
            entity.NotificationEmail = invoice.NotificationEmail;
            entity.BuyerInformation = Map<CreateInvoiceRequest, BuyerInformation>(invoice);
            entity.PaymentTolerance = storeBlob.PaymentTolerance;
            if (additionalTags != null)
                entity.InternalTags.AddRange(additionalTags);
            //Another way of passing buyer info to support
            FillBuyerInfo(invoice.Buyer, entity.BuyerInformation);
            if (entity?.BuyerInformation?.BuyerEmail != null)
            {
                if (!EmailValidator.IsEmail(entity.BuyerInformation.BuyerEmail))
                    throw new BitpayHttpException(400, "Invalid email");
                entity.RefundMail = entity.BuyerInformation.BuyerEmail;
            }

            var taxIncluded = invoice.TaxIncluded.HasValue ? invoice.TaxIncluded.Value : 0m;

            var currencyInfo = _CurrencyNameTable.GetNumberFormatInfo(invoice.Currency, false);
            if (currencyInfo != null)
            {
                int divisibility = currencyInfo.CurrencyDecimalDigits;
                invoice.Price = invoice.Price.RoundToSignificant(ref divisibility);
                divisibility = currencyInfo.CurrencyDecimalDigits;
                invoice.TaxIncluded = taxIncluded.RoundToSignificant(ref divisibility);
            }
            invoice.Price = Math.Max(0.0m, invoice.Price);
            invoice.TaxIncluded = Math.Max(0.0m, taxIncluded);
            invoice.TaxIncluded = Math.Min(taxIncluded, invoice.Price);

            entity.ProductInformation = Map<CreateInvoiceRequest, ProductInformation>(invoice);


            entity.RedirectURLTemplate = invoice.RedirectURL ?? store.StoreWebsite;

            entity.RedirectAutomatically =
                invoice.RedirectAutomatically.GetValueOrDefault(storeBlob.RedirectAutomatically);

            entity.Status = InvoiceStatus.New;
            entity.SpeedPolicy = ParseSpeedPolicy(invoice.TransactionSpeed, store.SpeedPolicy);

            HashSet<CurrencyPair> currencyPairsToFetch = new HashSet<CurrencyPair>();
            var rules = storeBlob.GetRateRules(_NetworkProvider);
            var excludeFilter = storeBlob.GetExcludedPaymentMethods(); // Here we can compose filters from other origin with PaymentFilter.Any()

            if (invoice.SupportedTransactionCurrencies != null && invoice.SupportedTransactionCurrencies.Count != 0)
            {
                var supportedTransactionCurrencies = invoice.SupportedTransactionCurrencies
                                                            .Where(c => c.Value.Enabled)
                                                            .Select(c => PaymentMethodId.TryParse(c.Key, out var p) ? p : null)
                                                            .ToHashSet();
                excludeFilter = PaymentFilter.Or(excludeFilter,
                                                 PaymentFilter.Where(p => !supportedTransactionCurrencies.Contains(p)));
            }

            foreach (var network in store.GetSupportedPaymentMethods(_NetworkProvider)
                                                .Where(s => !excludeFilter.Match(s.PaymentId))
                                                .Select(c => _NetworkProvider.GetNetwork<BTCPayNetworkBase>(c.PaymentId.CryptoCode))
                                                .Where(c => c != null))
            {
                currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, invoice.Currency));
                //TODO: abstract
                if (storeBlob.LightningMaxValue != null)
                    currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, storeBlob.LightningMaxValue.Currency));
                if (storeBlob.OnChainMinValue != null)
                    currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, storeBlob.OnChainMinValue.Currency));
            }

            var rateRules = storeBlob.GetRateRules(_NetworkProvider);
            var fetchingByCurrencyPair = _RateProvider.FetchRates(currencyPairsToFetch, rateRules, cancellationToken);
            var fetchingAll = WhenAllFetched(logs, fetchingByCurrencyPair);
            var supportedPaymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider)
                                               .Where(s => !excludeFilter.Match(s.PaymentId) && _paymentMethodHandlerDictionary.Support(s.PaymentId))
                                               .Select(c =>
                                                (Handler: _paymentMethodHandlerDictionary[c.PaymentId],
                                                SupportedPaymentMethod: c,
                                                Network: _NetworkProvider.GetNetwork<BTCPayNetworkBase>(c.PaymentId.CryptoCode)))
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
                errors.AppendLine("Warning: No wallet has been linked to your BTCPay Store. See the following link for more information on how to connect your store and wallet. (https://docs.btcpayserver.org/getting-started/connectwallet)");
                foreach (var error in logs.ToList())
                {
                    errors.AppendLine(error.ToString());
                }
                throw new BitpayHttpException(400, errors.ToString());
            }

            entity.SetSupportedPaymentMethods(supported);
            entity.SetPaymentMethods(paymentMethods);
            entity.PosData = invoice.PosData;

            foreach (var app in await getAppsTaggingStore)
            {
                entity.InternalTags.Add(AppService.GetAppInternalTag(app.Id));
            }

            using (logs.Measure("Saving invoice"))
            {
                entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity);
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await fetchingAll;
                }
                catch (AggregateException ex)
                {
                    ex.Handle(e => { logs.Write($"Error while fetching rates {ex}"); return true; });
                }
                await _InvoiceRepository.AddInvoiceLogs(entity.Id, logs);
            });
            _EventAggregator.Publish(new Events.InvoiceEvent(entity, 1001, InvoiceEvent.Created));
            var resp = entity.EntityToDTO();
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

        private async Task<PaymentMethod> CreatePaymentMethodAsync(Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair, IPaymentMethodHandler handler, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetworkBase network, InvoiceEntity entity, StoreData store, InvoiceLogs logs)
        {
            try
            {
                var logPrefix = $"{supportedPaymentMethod.PaymentId.ToPrettyString()}:";
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
                paymentMethod.PreferOnion = Uri.TryCreate(entity.ServerUrl, UriKind.Absolute, out var u) && u.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

                using (logs.Measure($"{logPrefix} Payment method details creation"))
                {
                    var paymentDetails = await handler.CreatePaymentMethodDetails(supportedPaymentMethod, paymentMethod, store, network, preparePayment);
                    paymentMethod.SetPaymentMethodDetails(paymentDetails);
                }

                var errorMessage = await
                    handler
                        .IsPaymentMethodAllowedBasedOnInvoiceAmount(storeBlob, fetchingByCurrencyPair,
                            paymentMethod.Calculate().Due, supportedPaymentMethod.PaymentId);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    logs.Write($"{logPrefix} {errorMessage}");
                    return null;
                }


#pragma warning disable CS0618
                if (paymentMethod.GetId().IsBTCOnChain)
                {
                    entity.TxFee = paymentMethod.NextNetworkFee;
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
