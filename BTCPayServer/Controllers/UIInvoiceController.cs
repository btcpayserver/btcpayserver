#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitpayClient;
using BitpayCreateInvoiceRequest = BTCPayServer.Models.BitpayCreateInvoiceRequest;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Filters.BitpayAPIConstraint(false)]
    public partial class UIInvoiceController : Controller
    {
        readonly InvoiceRepository _InvoiceRepository;
        readonly RateFetcher _RateProvider;
        readonly StoreRepository _StoreRepository;
        readonly UserManager<ApplicationUser> _UserManager;
        private readonly CurrencyNameTable _CurrencyNameTable;
        readonly EventAggregator _EventAggregator;
        readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly PullPaymentHostedService _paymentHostedService;
        private readonly LanguageService _languageService;
        private readonly ExplorerClientProvider _ExplorerClients;
        private readonly UIWalletsController _walletsController;
        private readonly LinkGenerator _linkGenerator;

        public WebhookSender WebhookNotificationManager { get; }

        public UIInvoiceController(
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            RateFetcher rateProvider,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            ContentSecurityPolicies csp,
            BTCPayNetworkProvider networkProvider,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            ApplicationDbContextFactory dbContextFactory,
            PullPaymentHostedService paymentHostedService,
            WebhookSender webhookNotificationManager,
            LanguageService languageService,
            ExplorerClientProvider explorerClients,
            UIWalletsController walletsController,
            LinkGenerator linkGenerator)
        {
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
            _UserManager = userManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _dbContextFactory = dbContextFactory;
            _paymentHostedService = paymentHostedService;
            WebhookNotificationManager = webhookNotificationManager;
            _languageService = languageService;
            this._ExplorerClients = explorerClients;
            _walletsController = walletsController;
            _linkGenerator = linkGenerator;
        }


        internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(BitpayCreateInvoiceRequest invoice,
            StoreData store, string serverUrl, List<string>? additionalTags = null,
            CancellationToken cancellationToken = default, Action<InvoiceEntity>? entityManipulator = null)
        {
            var entity = await CreateInvoiceCoreRaw(invoice, store, serverUrl, additionalTags, cancellationToken, entityManipulator);
            var resp = entity.EntityToDTO();
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        internal async Task<InvoiceEntity> CreateInvoiceCoreRaw(BitpayCreateInvoiceRequest invoice, StoreData store, string serverUrl, List<string>? additionalTags = null, CancellationToken cancellationToken = default,  Action<InvoiceEntity>? entityManipulator = null)
        {
            var storeBlob = store.GetStoreBlob();
            var entity = _InvoiceRepository.CreateNewInvoice();
            entity.ExpirationTime = invoice.ExpirationTime is DateTimeOffset v ? v : entity.InvoiceTime + storeBlob.InvoiceExpiration;
            entity.MonitoringExpiration = entity.ExpirationTime + storeBlob.MonitoringExpiration;
            if (entity.ExpirationTime - TimeSpan.FromSeconds(30.0) < entity.InvoiceTime)
            {
                throw new BitpayHttpException(400, "The expirationTime is set too soon");
            }
            entity.Metadata.OrderId = invoice.OrderId;
            entity.Metadata.PosData = invoice.PosData;
            entity.ServerUrl = serverUrl;
            entity.FullNotifications = invoice.FullNotifications || invoice.ExtendedNotifications;
            entity.ExtendedNotifications = invoice.ExtendedNotifications;
            entity.NotificationURLTemplate = invoice.NotificationURL;
            entity.NotificationEmail = invoice.NotificationEmail;
            if (additionalTags != null)
                entity.InternalTags.AddRange(additionalTags);
            FillBuyerInfo(invoice, entity);

            var taxIncluded = invoice.TaxIncluded.HasValue ? invoice.TaxIncluded.Value : 0m;
            var price = invoice.Price;

            entity.Metadata.ItemCode = invoice.ItemCode;
            entity.Metadata.ItemDesc = invoice.ItemDesc;
            entity.Metadata.Physical = invoice.Physical;
            entity.Metadata.TaxIncluded = invoice.TaxIncluded;
            entity.Currency = invoice.Currency;
            if (price is decimal vv)
            {
                entity.Price = vv;
                entity.Type = InvoiceType.Standard;
            }
            else
            {
                entity.Price = 0m;
                entity.Type = InvoiceType.TopUp;
            }

            entity.RedirectURLTemplate = invoice.RedirectURL ?? store.StoreWebsite;
            entity.RedirectAutomatically =
                invoice.RedirectAutomatically.GetValueOrDefault(storeBlob.RedirectAutomatically);
            entity.RequiresRefundEmail = invoice.RequiresRefundEmail;
            entity.CheckoutFormId = invoice.CheckoutFormId;
            entity.SpeedPolicy = ParseSpeedPolicy(invoice.TransactionSpeed, store.SpeedPolicy);

            IPaymentFilter? excludeFilter = null;
            if (invoice.PaymentCurrencies?.Any() is true)
            {
                invoice.SupportedTransactionCurrencies ??=
                    new Dictionary<string, InvoiceSupportedTransactionCurrency>();
                foreach (string paymentCurrency in invoice.PaymentCurrencies)
                {
                    invoice.SupportedTransactionCurrencies.TryAdd(paymentCurrency,
                        new InvoiceSupportedTransactionCurrency() { Enabled = true });
                }
            }
            if (invoice.SupportedTransactionCurrencies != null && invoice.SupportedTransactionCurrencies.Count != 0)
            {
                var supportedTransactionCurrencies = invoice.SupportedTransactionCurrencies
                                                            .Where(c => c.Value.Enabled)
                                                            .Select(c => PaymentMethodId.TryParse(c.Key, out var p) ? p : null)
                                                            .Where(c => c != null)
                                                            .ToHashSet();
                excludeFilter = PaymentFilter.Where(p => !supportedTransactionCurrencies.Contains(p));
            }
            entity.PaymentTolerance = storeBlob.PaymentTolerance;
            entity.DefaultPaymentMethod = invoice.DefaultPaymentMethod;
            entity.RequiresRefundEmail = invoice.RequiresRefundEmail;

            return await CreateInvoiceCoreRaw(entity, store, excludeFilter, null, cancellationToken, entityManipulator);
        }

        internal async Task<InvoiceEntity> CreatePaymentRequestInvoice(ViewPaymentRequestViewModel pr, decimal? amount, StoreData storeData, HttpRequest request, CancellationToken cancellationToken)
        {
            if (pr.AllowCustomPaymentAmounts && amount != null)
                amount = Math.Min(pr.AmountDue, amount.Value);
            else
                amount = pr.AmountDue;
            var redirectUrl = _linkGenerator.PaymentRequestLink(pr.Id, request.Scheme, request.Host, request.PathBase);

            var invoiceMetadata =
                new InvoiceMetadata
                {
                    OrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(pr.Id),
                    PaymentRequestId = pr.Id,
                    BuyerEmail = pr.Email
                };

            var invoiceRequest =
                new CreateInvoiceRequest
                {
                    Metadata = invoiceMetadata.ToJObject(),
                    Currency = pr.Currency,
                    Amount = amount,
                    Checkout = { RedirectURL = redirectUrl }
                };

            var additionalTags = new List<string> { PaymentRequestRepository.GetInternalTag(pr.Id) };
            return await CreateInvoiceCoreRaw(invoiceRequest, storeData, request.GetAbsoluteRoot(), additionalTags, cancellationToken);
        }

        internal async Task<InvoiceEntity> CreateInvoiceCoreRaw(CreateInvoiceRequest invoice, StoreData store, string serverUrl, List<string>? additionalTags = null, CancellationToken cancellationToken = default,  Action<InvoiceEntity>? entityManipulator = null)
        {
            var storeBlob = store.GetStoreBlob();
            var entity = _InvoiceRepository.CreateNewInvoice();
            entity.ServerUrl = serverUrl;
            entity.ExpirationTime = entity.InvoiceTime + (invoice.Checkout.Expiration ?? storeBlob.InvoiceExpiration);
            entity.MonitoringExpiration = entity.ExpirationTime + (invoice.Checkout.Monitoring ?? storeBlob.MonitoringExpiration);
            entity.ReceiptOptions = invoice.Receipt ?? new InvoiceDataBase.ReceiptOptions();
            if (invoice.Metadata != null)
                entity.Metadata = InvoiceMetadata.FromJObject(invoice.Metadata);
            invoice.Checkout ??= new CreateInvoiceRequest.CheckoutOptions();
            entity.Currency = invoice.Currency;
            if (invoice.Amount is decimal v)
            {
                entity.Price = v;
                entity.Type = InvoiceType.Standard;
            }
            else
            {
                entity.Price = 0.0m;
                entity.Type = InvoiceType.TopUp;
            }
            entity.SpeedPolicy = invoice.Checkout.SpeedPolicy ?? store.SpeedPolicy;
            entity.DefaultLanguage = invoice.Checkout.DefaultLanguage;
            entity.DefaultPaymentMethod = invoice.Checkout.DefaultPaymentMethod;
            entity.RedirectAutomatically = invoice.Checkout.RedirectAutomatically ?? storeBlob.RedirectAutomatically;
            entity.CheckoutFormId = invoice.Checkout.CheckoutFormId;
            entity.CheckoutType = invoice.Checkout.CheckoutType;
            entity.RequiresRefundEmail = invoice.Checkout.RequiresRefundEmail;
            IPaymentFilter? excludeFilter = null;
            if (invoice.Checkout.PaymentMethods != null)
            {
                var supportedTransactionCurrencies = invoice.Checkout.PaymentMethods
                                                            .Select(c => PaymentMethodId.TryParse(c, out var p) ? p : null)
                                                            .ToHashSet();
                excludeFilter = PaymentFilter.Where(p => !supportedTransactionCurrencies.Contains(p));
            }
            entity.PaymentTolerance = invoice.Checkout.PaymentTolerance ?? storeBlob.PaymentTolerance;
            entity.RedirectURLTemplate = invoice.Checkout.RedirectURL?.Trim();
            entity.RequiresRefundEmail = invoice.Checkout.RequiresRefundEmail;
            if (additionalTags != null)
                entity.InternalTags.AddRange(additionalTags);
            return await CreateInvoiceCoreRaw(entity, store, excludeFilter, invoice.AdditionalSearchTerms, cancellationToken, entityManipulator);
        }

        internal async Task<InvoiceEntity> CreateInvoiceCoreRaw(InvoiceEntity entity, StoreData store, IPaymentFilter? invoicePaymentMethodFilter, string[]? additionalSearchTerms = null, CancellationToken cancellationToken = default,  Action<InvoiceEntity>? entityManipulator = null)
        {
            InvoiceLogs logs = new InvoiceLogs();
            logs.Write("Creation of invoice starting", InvoiceEventData.EventSeverity.Info);
            var storeBlob = store.GetStoreBlob();
            if (string.IsNullOrEmpty(entity.Currency))
                entity.Currency = storeBlob.DefaultCurrency;
            entity.Currency = entity.Currency.Trim().ToUpperInvariant();
            entity.Price = Math.Max(0.0m, entity.Price);
            var currencyInfo = _CurrencyNameTable.GetNumberFormatInfo(entity.Currency, false);
            if (currencyInfo != null)
            {
                entity.Price = entity.Price.RoundToSignificant(currencyInfo.CurrencyDecimalDigits);
            }
            if (entity.Metadata.TaxIncluded is decimal taxIncluded)
            {
                if (currencyInfo != null)
                {
                    taxIncluded = taxIncluded.RoundToSignificant(currencyInfo.CurrencyDecimalDigits);
                }
                taxIncluded = Math.Max(0.0m, taxIncluded);
                taxIncluded = Math.Min(taxIncluded, entity.Price);
                entity.Metadata.TaxIncluded = taxIncluded;
            }

            var getAppsTaggingStore = _InvoiceRepository.GetAppsTaggingStore(store.Id);

            if (entity.Metadata.BuyerEmail != null)
            {
                if (!MailboxAddressValidator.IsMailboxAddress(entity.Metadata.BuyerEmail))
                    throw new BitpayHttpException(400, "Invalid email");
                entity.RefundMail = entity.Metadata.BuyerEmail;
            }
            entity.Status = InvoiceStatusLegacy.New;
            HashSet<CurrencyPair> currencyPairsToFetch = new HashSet<CurrencyPair>();
            var rules = storeBlob.GetRateRules(_NetworkProvider);
            var excludeFilter = storeBlob.GetExcludedPaymentMethods(); // Here we can compose filters from other origin with PaymentFilter.Any()
            if (invoicePaymentMethodFilter != null)
            {
                excludeFilter = PaymentFilter.Or(excludeFilter,
                                                 invoicePaymentMethodFilter);
            }
            foreach (var network in store.GetSupportedPaymentMethods(_NetworkProvider)
                                                .Where(s => !excludeFilter.Match(s.PaymentId))
                                                .Select(c => _NetworkProvider.GetNetwork<BTCPayNetworkBase>(c.PaymentId.CryptoCode))
                                                .Where(c => c != null))
            {
                currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, entity.Currency));
                foreach (var paymentMethodCriteria in storeBlob.PaymentMethodCriteria)
                {
                    if (paymentMethodCriteria.Value != null)
                    {
                        currencyPairsToFetch.Add(new CurrencyPair(network.CryptoCode, paymentMethodCriteria.Value.Currency));
                    }
                }
            }

            var rateRules = storeBlob.GetRateRules(_NetworkProvider);
            var fetchingByCurrencyPair = _RateProvider.FetchRates(currencyPairsToFetch, rateRules, cancellationToken);
            var fetchingAll = WhenAllFetched(logs, fetchingByCurrencyPair);

            List<ISupportedPaymentMethod> supported = new List<ISupportedPaymentMethod>();
            var paymentMethods = new PaymentMethodDictionary();

            bool noNeedForMethods = entity.Type != InvoiceType.TopUp && entity.Price == 0m;

            if (!noNeedForMethods)
            {
                // This loop ends with .ToList so we are querying all payment methods at once
                // instead of sequentially to improve response time
                var x1 = store.GetSupportedPaymentMethods(_NetworkProvider)
                    .Where(s => !excludeFilter.Match(s.PaymentId) &&
                                _paymentMethodHandlerDictionary.Support(s.PaymentId))
                    .Select(c =>
                        (Handler: _paymentMethodHandlerDictionary[c.PaymentId],
                            SupportedPaymentMethod: c,
                            Network: _NetworkProvider.GetNetwork<BTCPayNetworkBase>(c.PaymentId.CryptoCode)))
                    .Where(c => c.Network != null).ToList();
                var pmis = x1.Select(tuple => tuple.SupportedPaymentMethod.PaymentId).ToHashSet();
                foreach (var o in x1
                    .Select(o =>
                        (SupportedPaymentMethod: o.SupportedPaymentMethod,
                            PaymentMethod: CreatePaymentMethodAsync(fetchingByCurrencyPair, o.Handler,
                                o.SupportedPaymentMethod, o.Network, entity, store, logs, pmis)))
                    .ToList())
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
                    if (!store.GetSupportedPaymentMethods(_NetworkProvider).Any())
                        errors.AppendLine(
                            "Warning: No wallet has been linked to your BTCPay Store. See the following link for more information on how to connect your store and wallet. (https://docs.btcpayserver.org/WalletSetup/)");
                    else
                        errors.AppendLine("Warning: You have payment methods configured but none of them match any of the requested payment methods or the rate is not available. See logs below:");
                    foreach (var error in logs.ToList())
                    {
                        errors.AppendLine(error.ToString());
                    }

                    throw new BitpayHttpException(400, errors.ToString());
                }
            }
            entity.SetSupportedPaymentMethods(supported);
            entity.SetPaymentMethods(paymentMethods);
            foreach (var app in await getAppsTaggingStore)
            {
                entity.InternalTags.Add(AppService.GetAppInternalTag(app.Id));
            }

            if (entityManipulator != null)
            {
                entityManipulator.Invoke(entity);
            }
            using (logs.Measure("Saving invoice"))
            {
                entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity, additionalSearchTerms);
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await fetchingAll;
                }
                catch (AggregateException ex)
                {
                    ex.Handle(e => { logs.Write($"Error while fetching rates {ex}", InvoiceEventData.EventSeverity.Error); return true; });
                }
                await _InvoiceRepository.AddInvoiceLogs(entity.Id, logs);
            });
            _EventAggregator.Publish(new Events.InvoiceEvent(entity, InvoiceEvent.Created));
            return entity;
        }

        private Task WhenAllFetched(InvoiceLogs logs, Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair)
        {
            return Task.WhenAll(fetchingByCurrencyPair.Select(async pair =>
            {
                var rateResult = await pair.Value;
                logs.Write($"{pair.Key}: The rating rule is {rateResult.Rule}", InvoiceEventData.EventSeverity.Info);
                logs.Write($"{pair.Key}: The evaluated rating rule is {rateResult.EvaluatedRule}", InvoiceEventData.EventSeverity.Info);
                if (rateResult.Errors.Count != 0)
                {
                    var allRateRuleErrors = string.Join(", ", rateResult.Errors.ToArray());
                    logs.Write($"{pair.Key}: Rate rule error ({allRateRuleErrors})", InvoiceEventData.EventSeverity.Error);
                }
                foreach (var ex in rateResult.ExchangeExceptions)
                {
                    logs.Write($"{pair.Key}: Exception reaching exchange {ex.ExchangeName} ({ex.Exception.Message})", InvoiceEventData.EventSeverity.Error);
                }
            }).ToArray());
        }

        private async Task<PaymentMethod?> CreatePaymentMethodAsync(
            Dictionary<CurrencyPair, Task<RateResult>> fetchingByCurrencyPair,
            IPaymentMethodHandler handler, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetworkBase network,
            InvoiceEntity entity,
            StoreData store, InvoiceLogs logs,
            HashSet<PaymentMethodId> invoicePaymentMethods)
        {
            try
            {
                var logPrefix = $"{supportedPaymentMethod.PaymentId.ToPrettyString()}:";
                var storeBlob = store.GetStoreBlob();

                object? preparePayment;
                if (storeBlob.LazyPaymentMethods)
                {
                    preparePayment = null;
                }
                else
                {
                    preparePayment = handler.PreparePayment(supportedPaymentMethod, store, network);
                }
                var rate = await fetchingByCurrencyPair[new CurrencyPair(network.CryptoCode, entity.Currency)];
                if (rate.BidAsk == null)
                {
                    return null;
                }
                var paymentMethod = new PaymentMethod
                {
                    ParentEntity = entity,
                    Network = network,
                    Rate = rate.BidAsk.Bid,
                    PreferOnion = Uri.TryCreate(entity.ServerUrl, UriKind.Absolute, out var u) && u.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase)
                };
                paymentMethod.SetId(supportedPaymentMethod.PaymentId);

                using (logs.Measure($"{logPrefix} Payment method details creation"))
                {
                    var paymentDetails = await handler.CreatePaymentMethodDetails(logs, supportedPaymentMethod, paymentMethod, store, network, preparePayment, invoicePaymentMethods);
                    paymentMethod.SetPaymentMethodDetails(paymentDetails);
                }

                var criteria = storeBlob.PaymentMethodCriteria?.Find(methodCriteria => methodCriteria.PaymentMethod == supportedPaymentMethod.PaymentId);
                if (criteria?.Value != null && entity.Type != InvoiceType.TopUp)
                {
                    var currentRateToCrypto =
                        await fetchingByCurrencyPair[new CurrencyPair(supportedPaymentMethod.PaymentId.CryptoCode, criteria.Value.Currency)];
                    if (currentRateToCrypto?.BidAsk != null)
                    {
                        var amount = paymentMethod.Calculate().Due.GetValue(network as BTCPayNetwork);
                        var limitValueCrypto = criteria.Value.Value / currentRateToCrypto.BidAsk.Bid;

                        if (amount < limitValueCrypto && criteria.Above)
                        {
                            logs.Write($"{logPrefix} invoice amount below accepted value for payment method", InvoiceEventData.EventSeverity.Error);
                            return null;
                        }
                        if (amount > limitValueCrypto && !criteria.Above)
                        {
                            logs.Write($"{logPrefix} invoice amount above accepted value for payment method", InvoiceEventData.EventSeverity.Error);
                            return null;
                        }
                    }
                    else
                    {
                        var suffix = currentRateToCrypto?.EvaluatedRule is string s ? $" ({s})" : string.Empty;
                        logs.Write($"{logPrefix} This payment method should be created only if the amount of this invoice is in proper range. However, we are unable to fetch the rate of those limits. {suffix}", InvoiceEventData.EventSeverity.Warning);
                    }
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
                logs.Write($"{supportedPaymentMethod.PaymentId.CryptoCode}: Payment method unavailable ({ex.Message})", InvoiceEventData.EventSeverity.Error);
            }
            catch (Exception ex)
            {
                logs.Write($"{supportedPaymentMethod.PaymentId.CryptoCode}: Unexpected exception ({ex})", InvoiceEventData.EventSeverity.Error);
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

        private void FillBuyerInfo(BitpayCreateInvoiceRequest req, InvoiceEntity invoiceEntity)
        {
            var buyerInformation = invoiceEntity.Metadata;
            buyerInformation.BuyerAddress1 = req.BuyerAddress1;
            buyerInformation.BuyerAddress2 = req.BuyerAddress2;
            buyerInformation.BuyerCity = req.BuyerCity;
            buyerInformation.BuyerCountry = req.BuyerCountry;
            buyerInformation.BuyerEmail = req.BuyerEmail;
            buyerInformation.BuyerName = req.BuyerName;
            buyerInformation.BuyerPhone = req.BuyerPhone;
            buyerInformation.BuyerState = req.BuyerState;
            buyerInformation.BuyerZip = req.BuyerZip;
            var buyer = req.Buyer;
            if (buyer == null)
                return;
            buyerInformation.BuyerAddress1 ??= buyer.Address1;
            buyerInformation.BuyerAddress2 ??= buyer.Address2;
            buyerInformation.BuyerCity ??= buyer.City;
            buyerInformation.BuyerCountry ??= buyer.country;
            buyerInformation.BuyerEmail ??= buyer.email;
            buyerInformation.BuyerName ??= buyer.Name;
            buyerInformation.BuyerPhone ??= buyer.phone;
            buyerInformation.BuyerState ??= buyer.State;
            buyerInformation.BuyerZip ??= buyer.zip;
        }
    }
}
