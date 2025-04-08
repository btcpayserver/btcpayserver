#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;
using Serilog.Filters;
using BTCPayServer.Payouts;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Controllers
{
    [Filters.BitpayAPIConstraint(false)]
    public partial class UIInvoiceController : Controller
    {
        readonly InvoiceRepository _InvoiceRepository;
        private readonly WalletRepository _walletRepository;
        readonly RateFetcher _RateProvider;
        readonly StoreRepository _StoreRepository;
        readonly UserManager<ApplicationUser> _UserManager;
        private readonly CurrencyNameTable _CurrencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        readonly EventAggregator _EventAggregator;
        readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly PullPaymentHostedService _paymentHostedService;
        private readonly LanguageService _languageService;
        private readonly ExplorerClientProvider _ExplorerClients;
        private readonly UIWalletsController _walletsController;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly LinkGenerator _linkGenerator;
        private readonly IAuthorizationService _authorizationService;
        private readonly TransactionLinkProviders _transactionLinkProviders;
        private readonly Dictionary<PaymentMethodId, ICheckoutModelExtension> _paymentModelExtensions;
        private readonly PrettyNameProvider _prettyName;
        private readonly AppService _appService;
        private readonly IFileService _fileService;
        private readonly UriResolver _uriResolver;

        public WebhookSender WebhookNotificationManager { get; }
        public IEnumerable<IGlobalCheckoutModelExtension> GlobalCheckoutModelExtensions { get; }
        public IStringLocalizer StringLocalizer { get; }

        public UIInvoiceController(
            InvoiceRepository invoiceRepository,
            WalletRepository walletRepository,
            DisplayFormatter displayFormatter,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            RateFetcher rateProvider,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            ContentSecurityPolicies csp,
            BTCPayNetworkProvider networkProvider,
            PayoutMethodHandlerDictionary payoutHandlers,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            ApplicationDbContextFactory dbContextFactory,
            PullPaymentHostedService paymentHostedService,
            WebhookSender webhookNotificationManager,
            LanguageService languageService,
            ExplorerClientProvider explorerClients,
            UIWalletsController walletsController,
            InvoiceActivator invoiceActivator,
            LinkGenerator linkGenerator,
            AppService appService,
            IFileService fileService,
            UriResolver uriResolver,
            DefaultRulesCollection defaultRules,
            IAuthorizationService authorizationService,
            TransactionLinkProviders transactionLinkProviders,
            Dictionary<PaymentMethodId, ICheckoutModelExtension> paymentModelExtensions,
            IEnumerable<IGlobalCheckoutModelExtension> globalCheckoutModelExtensions,
            IStringLocalizer stringLocalizer,
            PrettyNameProvider prettyName)
        {
            _displayFormatter = displayFormatter;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _walletRepository = walletRepository;
            _RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
            _UserManager = userManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            this._payoutHandlers = payoutHandlers;
            _handlers = paymentMethodHandlerDictionary;
            _dbContextFactory = dbContextFactory;
            _paymentHostedService = paymentHostedService;
            WebhookNotificationManager = webhookNotificationManager;
            _languageService = languageService;
            this._ExplorerClients = explorerClients;
            _walletsController = walletsController;
            _invoiceActivator = invoiceActivator;
            _linkGenerator = linkGenerator;
            _authorizationService = authorizationService;
            _transactionLinkProviders = transactionLinkProviders;
            _paymentModelExtensions = paymentModelExtensions;
            GlobalCheckoutModelExtensions = globalCheckoutModelExtensions;
            _prettyName = prettyName;
            _fileService = fileService;
            _uriResolver = uriResolver;
            _defaultRules = defaultRules;
            _appService = appService;
            StringLocalizer = stringLocalizer;
        }

        internal async Task<InvoiceEntity> CreatePaymentRequestInvoice(Data.PaymentRequestData prData, decimal? amount, decimal amountDue, StoreData storeData, HttpRequest request, CancellationToken cancellationToken)
        {
            var id = prData.Id;
            var prBlob = prData.GetBlob();
            if (prBlob.AllowCustomPaymentAmounts && amount != null)
                amount = Math.Min(amountDue, amount.Value);
            else
                amount = amountDue;
            var redirectUrl = _linkGenerator.PaymentRequestLink(id, request.Scheme, request.Host, request.PathBase);

            JObject invoiceMetadata = prData.GetBlob()?.FormResponse ?? new JObject();
            invoiceMetadata.Merge(new InvoiceMetadata
            {
                OrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(id),
                PaymentRequestId = id,
                BuyerEmail = invoiceMetadata.TryGetValue("buyerEmail", out var formEmail) && formEmail.Type == JTokenType.String ? formEmail.Value<string>() :
                    string.IsNullOrEmpty(prBlob.Email) ? null : prBlob.Email
            }.ToJObject(), new JsonMergeSettings() { MergeNullValueHandling = MergeNullValueHandling.Ignore });

            var invoiceRequest =
                new CreateInvoiceRequest
                {
                    Metadata = invoiceMetadata,
                    Currency = prData.Currency,
                    Amount = amount,
                    Checkout = { RedirectURL = redirectUrl },
                    Receipt = new InvoiceDataBase.ReceiptOptions { Enabled = false }
                };

            var additionalTags = new List<string> { PaymentRequestRepository.GetInternalTag(id) };
            return await CreateInvoiceCoreRaw(invoiceRequest, storeData, request.GetAbsoluteRoot(), additionalTags, cancellationToken);
        }

        [NonAction]
        public async Task<InvoiceEntity> CreateInvoiceCoreRaw(CreateInvoiceRequest invoice, StoreData store, string serverUrl, List<string>? additionalTags = null, CancellationToken cancellationToken = default, Action<InvoiceEntity>? entityManipulator = null)
        {
            var storeBlob = store.GetStoreBlob();
            var entity = _InvoiceRepository.CreateNewInvoice(store.Id);
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
            if (invoice.Checkout.DefaultPaymentMethod is not null && PaymentMethodId.TryParse(invoice.Checkout.DefaultPaymentMethod, out var paymentMethodId))
            {
                entity.DefaultPaymentMethod = paymentMethodId;
            }
            entity.RedirectAutomatically = invoice.Checkout.RedirectAutomatically ?? storeBlob.RedirectAutomatically;
            entity.LazyPaymentMethods = invoice.Checkout.LazyPaymentMethods ?? storeBlob.LazyPaymentMethods;
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
            if (additionalTags != null)
                entity.InternalTags.AddRange(additionalTags);
            return await CreateInvoiceCoreRaw(entity, store, excludeFilter, invoice.AdditionalSearchTerms, cancellationToken, entityManipulator);
        }

        internal async Task<InvoiceEntity> CreateInvoiceCoreRaw(InvoiceEntity entity, StoreData store, IPaymentFilter? invoicePaymentMethodFilter, string[]? additionalSearchTerms = null, CancellationToken cancellationToken = default, Action<InvoiceEntity>? entityManipulator = null)
        {
            InvoiceLogs logs = new InvoiceLogs();
            logs.Write("Creation of invoice starting", InvoiceEventData.EventSeverity.Info);
            var storeBlob = store.GetStoreBlob();
            if (string.IsNullOrEmpty(entity.Currency))
                entity.Currency = storeBlob.DefaultCurrency;
            entity.Currency = entity.Currency.Trim().ToUpperInvariant();
            entity.Price = Math.Min(GreenfieldConstants.MaxAmount, entity.Price);
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
            entity.Status = InvoiceStatus.New;
            entity.UpdateTotals();


            var creationContext = new InvoiceCreationContext(store, storeBlob, entity, logs, _handlers, invoicePaymentMethodFilter);
            creationContext.SetLazyActivation(entity.LazyPaymentMethods);
            foreach (var term in additionalSearchTerms ?? Array.Empty<string>())
                creationContext.AdditionalSearchTerms.Add(term);

            if (entity.Type == InvoiceType.TopUp || entity.Price != 0m)
            {
                await creationContext.BeforeFetchingRates();
                await FetchRates(creationContext, cancellationToken);

                await creationContext.CreatePaymentPrompts();
                var contexts = creationContext.PaymentMethodContexts
                                              .Where(s => s.Value.Status is PaymentMethodContext.ContextStatus.WaitingForActivation or PaymentMethodContext.ContextStatus.Created)
                                              .Select(s => s.Value)
                                              .ToList();
                if (contexts.Count == 0)
                {
                    var message = new StringBuilder();
                    if (!store.GetPaymentMethodConfigs(_handlers).Any())
                        message.AppendLine(
                            "No wallet has been linked to your BTCPay Store. See the following link for more information on how to connect your store and wallet. (https://docs.btcpayserver.org/WalletSetup/)");
                    else
                    {
                        var list = logs.ToList();
                        var errors = list.Where(l => l.Severity == InvoiceEventData.EventSeverity.Error).Select(l => l.Log);
                        message.AppendLine("Error retrieving a matching payment method or rate.");
                        foreach (var error in errors)
                            message.AppendLine(error);
                    }
                    
                    throw new BitpayHttpException(400, message.ToString());
                }
                entity.SetPaymentPrompts(new PaymentPromptDictionary(contexts.Select(c => c.Prompt)));
            }
            else
            {
                entity.SetPaymentPrompts(new PaymentPromptDictionary());
            }

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
                await _InvoiceRepository.CreateInvoiceAsync(creationContext);
                await creationContext.ActivatingPaymentPrompt();
            }
            _ = _InvoiceRepository.AddInvoiceLogs(entity.Id, logs);
            _EventAggregator.Publish(new Events.InvoiceEvent(entity, InvoiceEvent.Created));
            return entity;
        }

        private async Task FetchRates(InvoiceCreationContext context, CancellationToken cancellationToken)
        {
            var rateRules = context.StoreBlob.GetRateRules(_defaultRules);
            await context.FetchingRates(_RateProvider, rateRules, cancellationToken);
        }
    }
}
