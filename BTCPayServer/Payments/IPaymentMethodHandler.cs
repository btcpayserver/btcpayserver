#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Migrations;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using crypto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using NBitcoin.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using InvoiceResponse = BTCPayServer.Models.InvoiceResponse;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// This class customize invoice creation by the creation of payment details for the PaymentMethod during invoice creation
    /// </summary>
    public interface IPaymentMethodHandler : IHandler<PaymentMethodId>
    {
        PaymentMethodId IHandler<PaymentMethodId>.Id => PaymentMethodId;
        PaymentMethodId PaymentMethodId { get; }
        /// <summary>
        /// The creation of the prompt details and prompt data
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        Task ConfigurePrompt(PaymentMethodContext context);
        /// <summary>
        /// Called before the fetching of the rates of an invoice.
        /// If the prompt is activated, it is recommended to start time consuming tasks here by setting the <see cref="PaymentMethodContext.State"/>.
        /// Those will be running while the rates are being fetched.
        /// Note that this can also be called ater rates has been fetched (for example in lazy activation or forced prompt renew)
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        Task BeforeFetchingRates(PaymentMethodContext context);
        /// <summary>
        /// Called after the invoice has been saved into database.
        /// Note that this can also be called ater rates has been fetched (for example in lazy activation or forced prompt renew)
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        Task AfterSavingInvoice(PaymentMethodContext context) => Task.CompletedTask;
        /// <summary>
        /// The serializer to use to serialize details and config into json
        /// </summary>
        JsonSerializer Serializer { get; }
        /// <summary>
        /// Parse the prompt details stored in the prompt
        /// </summary>
        /// <param name="details"></param>
        /// <returns></returns>
        object ParsePaymentPromptDetails(JToken details);
        /// <summary>
        /// Remove properties from the details which shouldn't appear to non-store owner.
        /// </summary>
        /// <param name="details">Prompt details</param>
        void StripDetailsForNonOwner(object details) { }

        /// <summary>
        /// Parse the configuration of the payment method in the store
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        object ParsePaymentMethodConfig(JToken config);

        Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext) => Task.CompletedTask;
        object ParsePaymentDetails(JToken details);
    }
    public class PaymentMethodConfigValidationContext
    {
        public record MissingPermissionError(string Permission, string Message);
        public PaymentMethodConfigValidationContext(IAuthorizationService authorizationService, ModelStateDictionary modelState, JToken config, ClaimsPrincipal user, JToken? previousConfig)
        {
            PreviousConfig = previousConfig;
            ModelState = modelState;
            AuthorizationService = authorizationService;
            Config = config;
            User = user;
        }
        public ClaimsPrincipal User { get; }
        public JToken? PreviousConfig { get; }
        public JToken Config { get; set; }
        public ModelStateDictionary ModelState { get; }
        public IAuthorizationService AuthorizationService { get; }
        public MissingPermissionError? MissingPermission { get; private set; }
        public bool StripUnknownProperties { get; set; } = true;

        public void SetMissingPermission(string permission, string message) => MissingPermission = new MissingPermissionError(permission, message);
    }

    public class InvoiceCreationContext
    {
        public InvoiceCreationContext(Data.StoreData store, Data.StoreBlob storeBlob, InvoiceEntity invoiceEntity, InvoiceLogs invoiceLogs, PaymentMethodHandlerDictionary handlers, IPaymentFilter? invoicePaymentMethodFilter)
        {
            PaymentMethodContexts = new Dictionary<PaymentMethodId, PaymentMethodContext>();
            InvoiceEntity = invoiceEntity;
            Logs = invoiceLogs;
            StoreBlob = storeBlob;
            var excludeFilter = storeBlob.GetExcludedPaymentMethods(); // Here we can compose filters from other origin with PaymentFilter.Any()
            if (invoicePaymentMethodFilter != null)
            {
                excludeFilter = PaymentFilter.Or(excludeFilter,
                                                 invoicePaymentMethodFilter);
            }
            foreach (var paymentMethodConfig in store.GetPaymentMethodConfigs())
            {
                var ctx = new PaymentMethodContext(store, storeBlob, paymentMethodConfig.Value, handlers[paymentMethodConfig.Key], invoiceEntity, invoiceLogs);
                PaymentMethodContexts.Add(paymentMethodConfig.Key, ctx);
                if (excludeFilter.Match(paymentMethodConfig.Key) || !handlers.Support(paymentMethodConfig.Key))
                    ctx.Status = PaymentMethodContext.ContextStatus.Excluded;
            }
        }
        public Dictionary<PaymentMethodId, PaymentMethodContext> PaymentMethodContexts
        {
            get;
        }
        public InvoiceEntity InvoiceEntity { get; }
        public InvoiceLogs Logs { get; }
        public Data.StoreBlob StoreBlob { get; }
        public HashSet<string> AdditionalSearchTerms { get; set; } = new HashSet<string>();

        public HashSet<string> GetAllSearchTerms()
        {
            return new HashSet<string>(PaymentMethodContexts.SelectMany(c => c.Value.AdditionalSearchTerms).Concat(AdditionalSearchTerms));
        }

        public Task BeforeFetchingRates()
        {
            return Task.WhenAll(PaymentMethodContexts.Select(c => c.Value.BeforeFetchingRates()));
        }

        public Task CreatePaymentPrompts()
        {
            return Task.WhenAll(PaymentMethodContexts.Select(c => c.Value.CreatePaymentPrompt()));
        }

        public HashSet<CurrencyPair> GetCurrenciesToFetch()
        {
            return new HashSet<CurrencyPair>(PaymentMethodContexts.SelectMany(c => c.Value.RequiredRates).Concat(PaymentMethodContexts.SelectMany(c => c.Value.OptionalRates)));
        }

        public void SetLazyActivation(bool lazy)
        {
            foreach (var p in PaymentMethodContexts)
                p.Value.Prompt.Inactive = lazy;
        }

        public Task ActivatingPaymentPrompt()
        {
            return Task.WhenAll(PaymentMethodContexts.Select(c => c.Value.ActivatingPaymentPrompt()));
        }

        public async Task FetchingRates(RateFetcher rateFetcher, RateRules rateRules, CancellationToken cancellationToken)
        {
            var currencyPairsToFetch = GetCurrenciesToFetch();
            var fetchingRates = rateFetcher.FetchRates(currencyPairsToFetch, rateRules, new StoreIdRateContext(InvoiceEntity.StoreId), cancellationToken);
            HashSet<CurrencyPair> failedRates = new HashSet<CurrencyPair>();
            foreach (var fetching in fetchingRates)
            {
                try
                {
                    var rateResult = await fetching.Value;
                    Logs.Write($"The rating rule is {rateResult.Rule}", InvoiceEventData.EventSeverity.Info);
                    Logs.Write($"The evaluated rating rule is {rateResult.EvaluatedRule}", InvoiceEventData.EventSeverity.Info);
                    if (rateResult is RateResult { BidAsk: { } bidAsk })
                    {
                        InvoiceEntity.AddRate(fetching.Key, bidAsk.Bid);
                    }
                    else
                    {
                        failedRates.Add(fetching.Key);
                        if (rateResult.Errors.Count != 0)
                        {
                            var allRateRuleErrors = string.Join(", ", rateResult.Errors.ToArray());
                            Logs.Write($"Rate rule error ({allRateRuleErrors})", InvoiceEventData.EventSeverity.Warning);
                        }
                        foreach (var exx in rateResult.ExchangeExceptions)
                        {
                            Logs.Write($"Error from exchange {exx.ExchangeName} ({exx.Exception.Message})", InvoiceEventData.EventSeverity.Warning);
                        }
                        Logs.Write($"Unable to get rate {fetching.Key}.", InvoiceEventData.EventSeverity.Warning);
                    }
                }
                catch (Exception ex)
                {
                    Logs.Write($"Error while fetching rates {ex}", InvoiceEventData.EventSeverity.Warning);
                    failedRates.Add(fetching.Key);
                }
            }

            foreach (var paymentContext in PaymentMethodContexts.Values)
            {
                var failedRequiredRates = failedRates.Where(r => paymentContext.RequiredRates.Contains(r)).ToHashSet();
                var failedOptionalRates = failedRates.Where(r => paymentContext.OptionalRates.Contains(r)).ToHashSet();
                if (failedRequiredRates.Count > 0)
                {
                    paymentContext.Status = PaymentMethodContext.ContextStatus.Failed;
                    paymentContext.Logs.Write($"Unable to get rate(s) {ToString(failedRequiredRates)}, this payment method disabled for this invoice.", InvoiceEventData.EventSeverity.Error);
                }
                if (failedOptionalRates.Count > 0)
                {
                    paymentContext.Logs.Write($"Unable to get rate(s) {ToString(failedRequiredRates)}.", InvoiceEventData.EventSeverity.Warning);
                }
            }
        }

        private string ToString<CurrencyPair>(HashSet<CurrencyPair> failedRequiredRates)
        {
            return string.Join(", ", failedRequiredRates);
        }
    }
    public class CurrencyPairSet : HashSet<CurrencyPair>
    {
        public CurrencyPairSet(string defaultCurrency)
        {
            DefaultCurrency = defaultCurrency;
        }

        public string DefaultCurrency { get; }

        public bool Add(string currency)
        {
            return this.Add(new CurrencyPair(currency, DefaultCurrency));
        }
    }
    public class PaymentMethodContext
    {
        public enum ContextStatus
        {
            WaitingForCreation,
            WaitingForActivation,
            Created,
            Failed,
            Excluded
        }
        public InvoiceEntity InvoiceEntity { get; }
        public PrefixedInvoiceLogs Logs { get; }
        public PaymentMethodId PaymentMethodId { get; }
        public JToken PaymentMethodConfig { get; }
        public IPaymentMethodHandler Handler { get; }
        public Data.StoreBlob StoreBlob { get; }
        public Data.StoreData Store { get; }
        public PaymentPrompt Prompt { get; set; }
        public PaymentMethodContext(
            Data.StoreData store,
            Data.StoreBlob storeBlob,
            JToken paymentMethodConfig,
            IPaymentMethodHandler handler,
            InvoiceEntity invoiceEntity,
            InvoiceLogs invoiceLogs)
        {
            Store = store;
            StoreBlob = storeBlob;
            InvoiceEntity = invoiceEntity;
            PaymentMethodId = handler.PaymentMethodId;
            Logs = new PrefixedInvoiceLogs(invoiceLogs, $"{PaymentMethodId.ToString()}: ");
            PaymentMethodConfig = paymentMethodConfig;
            Handler = handler;
            if (invoiceEntity.Currency is null)
                throw new InvalidOperationException("InvoiceEntity.Currency isn't initialized");
            RequiredRates = new CurrencyPairSet(invoiceEntity.Currency);
            OptionalRates = new CurrencyPairSet(invoiceEntity.Currency);
            Prompt = new PaymentPrompt() { ParentEntity = invoiceEntity, PaymentMethodId = PaymentMethodId };
        }
        public CurrencyPairSet RequiredRates { get; }
        public CurrencyPairSet OptionalRates { get; }
        public object? State { get; set; }
        public HashSet<String> AdditionalSearchTerms { get; set; } = new HashSet<string>();
        /// <summary>
        /// This string can be used to query AddressInvoice to find the invoiceId
        /// </summary>
        public List<string> TrackedDestinations { get; } = new();

        internal async Task BeforeFetchingRates()
        {
            await Handler.BeforeFetchingRates(this);
            // We need to fetch the rates necessary for the evaluation of the payment method criteria
            var currency = Prompt.Currency;
            if (currency is not null)
                RequiredRates.Add(currency);
            if (currency is not null 
                 && Status is PaymentMethodContext.ContextStatus.WaitingForCreation or PaymentMethodContext.ContextStatus.WaitingForActivation)
            {
                foreach (var paymentMethodCriteria in StoreBlob.PaymentMethodCriteria
                    .Where(c => c.Value?.Currency is not null && c.PaymentMethod == PaymentMethodId))
                {
                    RequiredRates.Add(new CurrencyPair(currency, paymentMethodCriteria.Value.Currency));
                }
            }
        }

        public Task ActivatingPaymentPrompt()
        {
            if (Status is not (ContextStatus.Created or ContextStatus.WaitingForActivation))
                return Task.CompletedTask;
            return Handler.AfterSavingInvoice(this);
        }

        private Task CreatingPaymentPrompt()
        {
            return Handler.ConfigurePrompt(this);
        }

        public async Task CreatePaymentPrompt()
        {
            if (Status != ContextStatus.WaitingForCreation)
                return;
            bool criteriaChecked = false;
            if (Prompt.Currency is not null)
            {
                if (!CheckCriteria())
                {
                    Status = ContextStatus.Failed;
                    return;
                }
                criteriaChecked = true;
            }
            if (!Prompt.Activated)
            {
                Status = ContextStatus.WaitingForActivation;
                return;
            }
            using (Logs.Measure("Payment method details creation"))
            {
                try
                {
                    await Handler.ConfigurePrompt(this);
                    Status = ContextStatus.Created;
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    Logs.Write($"Payment method unavailable ({ex.Message})", InvoiceEventData.EventSeverity.Error);
                    Status = ContextStatus.Failed;
                    return;
                }
                catch (Exception ex)
                {
                    Logs.Write($"Unexpected exception ({ex})", InvoiceEventData.EventSeverity.Error);
                    Status = ContextStatus.Failed;
                    return;
                }
            }
            if (!criteriaChecked && !CheckCriteria())
            {
                Status = ContextStatus.Failed;
                return;
            }
        }
        public ContextStatus Status { get; internal set; }
        private bool CheckCriteria()
        {
            var criteria = StoreBlob.PaymentMethodCriteria?.Find(methodCriteria => methodCriteria.PaymentMethod == Handler.PaymentMethodId);
            if (criteria?.Value != null && InvoiceEntity.Type != InvoiceType.TopUp)
            {
                try
                {
                    var currentRateToCrypto = InvoiceEntity.GetRate(new CurrencyPair(Prompt.Currency, criteria.Value.Currency));
                    var amount = Prompt.Calculate().Due;
                    var limitValueCrypto = criteria.Value.Value / currentRateToCrypto;

                    if (amount < limitValueCrypto && criteria.Above)
                    {
                        Logs.Write($"Invoice amount below accepted value for payment method", InvoiceEventData.EventSeverity.Error);
                        return false;
                    }
                    if (amount > limitValueCrypto && !criteria.Above)
                    {
                        Logs.Write($"Invoice amount above accepted value for payment method", InvoiceEventData.EventSeverity.Error);
                        return false;
                    }
                }
                catch
                {
                    Logs.Write($"This payment method should be created only if the amount of this invoice is in proper range. However, we are unable to fetch the rate of those limits.", InvoiceEventData.EventSeverity.Warning);
                    return false;
                }
            }
            return true;
        }
    }

    public class PrefixedInvoiceLogs
    {
        string _LogPrefix;
        public PrefixedInvoiceLogs(InvoiceLogs invoiceLogs, string prefix)
        {
            InvoiceLogs = invoiceLogs;
            _LogPrefix = prefix;
        }

        public void Write(string data, InvoiceEventData.EventSeverity eventSeverity)
        {
            InvoiceLogs.Write(_LogPrefix + data, eventSeverity);
        }

        internal IDisposable Measure(string logs)
        {
            return InvoiceLogs.Measure(_LogPrefix + logs);
        }

        public InvoiceLogs InvoiceLogs { get; }
    }
}
