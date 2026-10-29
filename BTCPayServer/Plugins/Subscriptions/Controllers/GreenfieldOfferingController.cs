#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.ModelBinders;
using BTCPayServer.Plugins.Monetization;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NicolasDorier.RateLimits;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Subscriptions.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield,
        Policy = SubscriptionsPolicies.CanViewOfferings)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldOfferingController(
        ApplicationDbContext ctx,
        IAuthorizationService authorizationService,
        SubscriptionHostedService subscriptionHostedService,
        CurrencyNameTable currencyNameTable,
        AppService appService) : ControllerBase
    {
        private const int MaxMetadataLength = 64 * 1024;
        private const int MaxFeatures = 100;
        internal const int MaxCreditDescriptionLength = 500;

        [HttpGet("~/api/v1/stores/{storeId}/offerings/{offeringId?}")]
        public async Task<IActionResult> GetOffering(string storeId, string? offeringId = null)
        {
            OfferingData[] offerings;
            if (offeringId is null)
            {
                offerings = await ctx.Offerings.IncludeAll().Where(o => o.App.StoreDataId == storeId).ToArrayAsync();
                await ctx.Plans.FetchPlanFeaturesAsync(offerings.SelectMany(p => p.Plans).ToArray());
            }
            else
            {
                var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId, fetchPlanFeatures: true);
                if (offering is null)
                    return OfferingNotFound();
                offerings = new[] { offering };
            }
            if (offeringId is not null)
                return Ok(Mapper.MapOffering(offerings[0]));
            return Ok(offerings.Select(Mapper.MapOffering).ToArray());
        }
        [HttpPost("~/api/v1/stores/{storeId}/offerings")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanModifyOfferings)]
        public async Task<IActionResult> CreateOffering(string storeId, [FromBody] OfferingModel request)
        {
            ValidateOffering(request);
            if (!ModelState.IsValid || request?.AppName is null)
                return this.CreateValidationError(ModelState);
            var o = await appService.CreateOffering(storeId, request.AppName);
            var offering = await ctx.Offerings.GetOfferingData(o.OfferingId, storeId);
            if (offering is not null)
            {
                offering.SuccessRedirectUrl = request.SuccessRedirectUrl;
                offering.Metadata = request.Metadata?.ToString() ?? "{}";
                if (request.Features is not null)
                {
                    UIOfferingController.UpdateFeatures(ctx, offering, new()
                    {
                        Features = request.Features.Select(f => new ConfigureOfferingViewModel.FeatureViewModel()
                            { Id = f.Id, ShortDescription = f.Description }).ToList()
                    });
                }
            }
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetOffering(storeId, offering?.Id ?? "");
        }

        [HttpPut("~/api/v1/stores/{storeId}/offerings/{offeringId}")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanModifyOfferings)]
        public async Task<IActionResult> UpdateOffering(string storeId, string offeringId, [FromBody] OfferingModel request)
        {
            ValidateOffering(request);
            if (!ModelState.IsValid || request?.AppName is null)
                return this.CreateValidationError(ModelState);

            var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
            if (offering is null)
                return OfferingNotFound();

            offering.App.Name = request.AppName ?? offering.App.Name;
            offering.SuccessRedirectUrl = request.SuccessRedirectUrl ?? offering.SuccessRedirectUrl;
            offering.Metadata = request.Metadata?.ToString() ?? offering.Metadata;

            if (request.Features is not null)
            {
                UIOfferingController.UpdateFeatures(ctx, offering, new()
                {
                    Features = request.Features.Select(f => new ConfigureOfferingViewModel.FeatureViewModel()
                    {
                        Id = f.Id,
                        ShortDescription = f.Description
                    }).ToList()
                });
            }

            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetOffering(storeId, offeringId);
        }

        private void ValidateOffering(OfferingModel? request)
        {
            if (request?.AppName is null)
                ModelState.AddModelError(nameof(request.AppName), "AppName is required");
            else if (request.AppName.Length > AppData.MaxNameLength)
                ModelState.AddModelError(nameof(request.AppName), $"AppName must be at most {AppData.MaxNameLength} characters");
            if (!string.IsNullOrEmpty(request?.SuccessRedirectUrl) &&
                !PlanCheckoutData.IsSafeRedirectUrl(request.SuccessRedirectUrl))
            {
                ModelState.AddModelError(nameof(request.SuccessRedirectUrl), "SuccessRedirectUrl must be an HTTP or HTTPS URL");
            }
            if (request?.SuccessRedirectUrl?.Length > OfferingData.MaxSuccessRedirectUrlLength)
                ModelState.AddModelError(nameof(request.SuccessRedirectUrl), "SuccessRedirectUrl is too long");
            ValidateMetadata(nameof(OfferingModel.Metadata), request?.Metadata);

            if (request?.Features is null)
                return;
            if (request.Features.Count > MaxFeatures)
                ModelState.AddModelError(nameof(request.Features), $"At most {MaxFeatures} features are allowed");
            for (var i = 0; i < request.Features.Count; i++)
            {
                var feature = request.Features[i];
                if (feature is null)
                {
                    ModelState.AddModelError($"{nameof(request.Features)}[{i}]", "Feature is required");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(feature.Id))
                    ModelState.AddModelError($"{nameof(request.Features)}[{i}].{nameof(feature.Id)}", "Feature id is required");
                else if (feature.Id.Length > FeatureData.MaxCustomIdLength)
                    ModelState.AddModelError($"{nameof(request.Features)}[{i}].{nameof(feature.Id)}", $"Feature id must be at most {FeatureData.MaxCustomIdLength} characters");
                if (feature.Description?.Length > FeatureData.MaxDescriptionLength)
                    ModelState.AddModelError($"{nameof(request.Features)}[{i}].{nameof(feature.Description)}", $"Feature description must be at most {FeatureData.MaxDescriptionLength} characters");
            }
        }

        [HttpPost("~/api/v1/stores/{storeId}/offerings/{offeringId}/plans")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanModifyOfferings)]
        public async Task<IActionResult> CreateOfferingPlan(string storeId, string offeringId, [FromBody] CreatePlanRequest request)
        {
            var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
            var store = await ctx.Stores.FindAsync(storeId);
            if (offering is null || store is null)
                return OfferingNotFound();
            if (request.Price < 0m)
                ModelState.AddModelError(nameof(request.Price), "Price cannot be negative");
            if (request?.Name is null)
                ModelState.AddModelError(nameof(request.Name), "Name is required");
            if (request?.Description?.Length > PlanData.MaxDescriptionLength)
                ModelState.AddModelError(nameof(request.Description), $"Description must be at most {PlanData.MaxDescriptionLength} characters");
            if (request?.Currency?.Length > CurrencyNameTable.MaxCurrencyCodeLength)
                ModelState.AddModelError(nameof(request.Currency), "Currency is too long");
            ValidateMetadata(nameof(CreatePlanRequest.Metadata), request?.Metadata);

            var features = new List<FeatureData>();
            if (request?.Features is not null)
            {
                foreach (var feature in request.Features.Distinct())
                {
                    var offeringFeature = offering.Features.FirstOrDefault(f => f.CustomId == feature);
                    if (offeringFeature is null)
                    {
                        ModelState.AddModelError(nameof(request.Features), "A feature with id '" + feature + "' does not exist on the offering.");
                        break;
                    }
                    features.Add(offeringFeature);
                }
            }
            if (!ModelState.IsValid || request?.Name is null)
                return this.CreateValidationError(ModelState);

            var data = new PlanData()
            {
                OfferingId = offeringId,
                Description = request.Description ?? "",
                Currency = store.GetStoreBlob().DefaultCurrency ?? request.Currency,
                GracePeriodDays = request.GracePeriodDays ?? 0,
                TrialDays = request.TrialDays ?? 0,
                Name = request.Name,
                Price = request.Price ?? 0m,
                Metadata = request.Metadata?.ToString() ?? "{}",
                RecurringType = Mapper.Map(request.RecurringType ?? OfferingPlanModel.RecurringInterval.Monthly),
            };

            if (request.OptimisticActivation is {} o)
                data.OptimisticActivation = o;
            if (request.Renewable is {} r)
                data.Renewable = r;
            ctx.Plans.Add(data);
            if (features.Count > 0)
            {
                foreach (var f in features)
                {
                    ctx.PlanFeatures.Add(new()
                    {
                        PlanId = data.Id,
                        FeatureId = f.Id
                    });
                }
            }

            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetOfferingPlan(storeId, offeringId, data.Id);
        }

        [HttpPut("~/api/v1/stores/{storeId}/offerings/{offeringId}/plans/{planId}")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanModifyOfferings)]
        public async Task<IActionResult> UpdateOfferingPlan(string storeId, string offeringId, string planId, [FromBody] CreatePlanRequest request)
        {
            var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
            if (offering is null)
                return OfferingNotFound();
            var store = await ctx.Stores.FindAsync(storeId);
            if (store is null)
                return OfferingNotFound();
            var plan = offering.Plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null)
                return PlanNotFound();
            if (request?.Price < 0m)
                ModelState.AddModelError(nameof(request.Price), "Price cannot be negative");
            if (request?.Description?.Length > PlanData.MaxDescriptionLength)
                ModelState.AddModelError(nameof(request.Description), $"Description must be at most {PlanData.MaxDescriptionLength} characters");
            if (request?.Currency?.Length > CurrencyNameTable.MaxCurrencyCodeLength)
                ModelState.AddModelError(nameof(request.Currency), "Currency is too long");
            ValidateMetadata(nameof(CreatePlanRequest.Metadata), request?.Metadata);

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            request ??= new();
            plan.Name = request.Name ?? plan.Name;
            plan.Description = request.Description ?? plan.Description;
            plan.Currency = request.Currency ?? plan.Currency;
            plan.GracePeriodDays = request.GracePeriodDays ?? plan.GracePeriodDays;
            plan.TrialDays = request.TrialDays ?? plan.TrialDays;
            plan.Price = request.Price ?? plan.Price;
            plan.Metadata = request.Metadata?.ToString() ?? plan.Metadata;
            if (request.RecurringType is not null)
                plan.RecurringType = Mapper.Map(request.RecurringType.Value);
            plan.OptimisticActivation = request.OptimisticActivation ?? plan.OptimisticActivation;
            plan.Renewable = request.Renewable ?? plan.Renewable;

            if (request.Features is not null)
            {
                var existingPlanFeatures = await ctx.PlanFeatures.Where(pf => pf.PlanId == plan.Id).ToArrayAsync();
                if (existingPlanFeatures.Length > 0)
                    ctx.PlanFeatures.RemoveRange(existingPlanFeatures);
                var offeringFeatures = await ctx.Offerings.Where(o => o.Id == plan.OfferingId).SelectMany(o => o.Features).ToArrayAsync();
                var featureByCustomId = offeringFeatures.ToDictionary(f => f.CustomId);
                foreach (var f in request.Features.Distinct())
                {
                    if (!featureByCustomId.TryGetValue(f ?? "", out var offeringFeature))
                    {
                        ModelState.AddModelError(nameof(request.Features), $"A feature with id '{f}' does not exist on the offering.");
                    }
                    else
                    {
                        ctx.PlanFeatures.Add(new()
                        {
                            PlanId = plan.Id,
                            FeatureId = offeringFeature.Id
                        });
                    }
                }
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetOfferingPlan(storeId, offeringId, plan.Id);
        }

        [HttpGet("~/api/v1/stores/{storeId}/offerings/{offeringId}/plans/{planId}")]
        public async Task<IActionResult> GetOfferingPlan(string storeId, string offeringId, string planId)
        {
            var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
            if (offering is null)
                return OfferingNotFound();
            var plan = offering.Plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null)
                return PlanNotFound();
            await plan.EnsureFeatureLoaded(ctx);
            return Ok(Mapper.MapPlan(plan));
        }

        [HttpGet("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}")]
        public async Task<IActionResult> GetSubscriber(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            await subscriber.Plan.EnsureFeatureLoaded(ctx);
            if (subscriber.NewPlan is not null)
                await subscriber.NewPlan.EnsureFeatureLoaded(ctx);
            return Ok(Mapper.MapToSubscriberModel(subscriber));
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpDelete("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}")]
        public async Task<IActionResult> DeleteSubscriber(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            ctx.Subscribers.Remove(subscriber);
            await ctx.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpGet("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/credits/{currency}")]
        public async Task<IActionResult> GetCredit(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector,
            string? currency)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            currency = currency == "current" ? subscriber.Plan.Currency : currency;
            return Ok(new CreditModel()
            {
                Currency = currency,
                Value = subscriber.GetCredit(currency)
            });
        }
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanCreditSubscribers)]
        [HttpPost("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/credits/{currency}")]
        public async Task<IActionResult> UpdateCredit(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector,
            string? currency,
            [FromBody] UpdateCreditRequest request)
        {
            var update = request ?? new UpdateCreditRequest();
            if (update.Description?.Length > MaxCreditDescriptionLength)
                ModelState.AddModelError(nameof(update.Description), $"Description must be at most {MaxCreditDescriptionLength} characters");
            if (update.Credit is < 0m or > GreenfieldConstants.MaxAmount)
                ModelState.AddModelError(nameof(update.Credit), $"Credit must be between 0 and {GreenfieldConstants.MaxAmount}");
            if (update.Charge is < 0m or > GreenfieldConstants.MaxAmount)
                ModelState.AddModelError(nameof(update.Charge), $"Charge must be between 0 and {GreenfieldConstants.MaxAmount}");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            currency = currency == "current" ? subscriber.Plan.Currency : currency;
            var newTotal = await subscriptionHostedService.UpdateCredit(new()
            {
                SubscriberId = subscriber.Id,
                Currency = currency,
                Description = update.Description,
                Credit = update.Credit,
                Charge = update.Charge,
                AllowOverdraft = update.AllowOverdraft
            });
            if (newTotal is null)
                return this.CreateAPIError(400, update.AllowOverdraft ? "balance-limit" : "overdraft",
                    "The subscriber's balance would exceed the permitted overdraft or balance limits.");
            ctx.ChangeTracker.Clear();
            return await GetCredit(storeId, offeringId, customerSelector, currency);
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpPost("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/suspend")]
        public async Task<IActionResult> SuspendSubscriber(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector,
            [FromBody] SuspendSubscriberRequest model)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            await subscriptionHostedService.Suspend(subscriber.Id, model?.Reason);
            ctx.ChangeTracker.Clear();
            return await GetSubscriber(storeId, offeringId, customerSelector);
        }
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpPost("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/unsuspend")]
        public async Task<IActionResult> UnsuspendSubscriber(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            await subscriptionHostedService.Unsuspend(subscriber.Id);
            ctx.ChangeTracker.Clear();
            return await GetSubscriber(storeId, offeringId, customerSelector);
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpPut("~/api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/dates")]
        public async Task<IActionResult> UpdateSubscriberDates(string storeId, string offeringId,
            [ModelBinder<CustomerSelectorModelBinder>]
            CustomerSelector customerSelector,
            [FromBody] UpdateSubscriberDatesRequest? request)
        {
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, customerSelector, storeId);
            if (subscriber is null)
                return SubscriberNotFound();
            if (request is null or { StartDate: null, ExpirationDate: null })
                return await GetSubscriber(storeId, offeringId, customerSelector);
            var startDate = (request.StartDate ?? subscriber.PlanStarted).ToUniversalTime();
            var expirationDate = request.ExpirationDate?.ToUniversalTime();
            if (expirationDate is { } exp && exp <= startDate)
                return this.CreateAPIError(400, "invalid-dates", "Expiration date must be after the start date.");
            await subscriptionHostedService.UpdateDates(subscriber.Id, startDate, expirationDate);
            ctx.ChangeTracker.Clear();
            return await GetSubscriber(storeId, offeringId, customerSelector);
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/plan-checkout/{checkoutId}")]
        public async Task<IActionResult> ProceedPlanCheckout(string checkoutId,
            [FromServices] MonetizationSettings monetizationSettings,
            [FromServices] IRateLimitService rateLimitService,
            [FromQuery] string? email = null)
        {
            var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
            if (checkout is null)
                return CheckoutNotFound();
            if (checkout.IsExpired)
                return CheckoutExpired();
            if (email is not null && !email.IsValidEmail())
                ModelState.AddModelError(nameof(email), "Invalid email");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            if (checkout is { Invoice: { Status: not (Data.InvoiceData.Expired or Data.InvoiceData.Invalid) } })
                return await GetPlanCheckout(checkoutId);

            if (checkout is { NewSubscriber: true, NewSubscriberEmail: null } && email is {})
            {
                if (checkout.Plan.OfferingId == monetizationSettings.OfferingId &&
                    !await rateLimitService.Throttle(ZoneLimits.Register, HttpContext.Connection.RemoteIpAddress, HttpContext.RequestAborted))
                    return new TooManyRequestsResult(ZoneLimits.Register);

                checkout.NewSubscriberEmail = email;
                await ctx.SaveChangesAsync();
            }

            if (checkout is { NewSubscriber: true, NewSubscriberEmail: null })
                ModelState.AddModelError(nameof(email), "You need to pass `email` as query string");

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            try
            {
                await subscriptionHostedService.ProceedToSubscribe(checkout.Id, HttpContext.RequestAborted);
            }
            catch (BitpayHttpException ex)
            {
                return this.CreateAPIError(400, "invoice-creation-error", ex.Message);
            }
            ctx.ChangeTracker.Clear();
            return await GetPlanCheckout(checkoutId);
        }

        // Intentional: expired sessions still return data so the client can show
        // "expired" data are cleaned up monthly.
        [AllowAnonymous]
        [HttpGet("~/api/v1/plan-checkout/{checkoutId}")]
        public async Task<IActionResult> GetPlanCheckout(string checkoutId)
        {
            var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
            if (checkout is null)
                return CheckoutNotFound();
            var model = Mapper.MapPlanCheckout(checkout);
            if (!(await CanManageSubscribers(checkout.Plan.Offering.App.StoreDataId)))
            {
                if (!checkout.PlanStarted)
                {
                    model.SuccessRedirectUrl = null;
                    model.RedirectUrl = null;
                    if (model.Subscriber is not null)
                        model.Subscriber.Offering.SuccessRedirectUrl = null;
                }
                model.Metadata = null;
                model.InvoiceMetadata = null;
                model.NewSubscriberMetadata = null;
                HideMerchantData(model.Plan);
                if (model.Subscriber is not null)
                    HideMerchantData(model.Subscriber, [model.Plan.Id]);
            }
            return Ok(model);
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = SubscriptionsPolicies.CanManageSubscribers)]
        [HttpPost("~/api/v1/plan-checkout")]
        public async Task<IActionResult> CreatePlanCheckout([FromBody]CreatePlanCheckoutRequest model)
        {
            var selector = GetSelector(nameof(model.CustomerSelector), model.CustomerSelector, false);
            if (model.StoreId is null)
                ModelState.AddModelError(nameof(model.StoreId), "StoreId is required");
            if (model.OfferingId is null)
                ModelState.AddModelError(nameof(model.OfferingId), "OfferingId is required");
            if (model.PlanId is null)
                ModelState.AddModelError(nameof(model.PlanId), "PlanId is required");
            if (model.NewSubscriberEmail is not null && model.CustomerSelector is not null)
                ModelState.AddModelError(nameof(model.NewSubscriberEmail), "If customerSelector is specified, newSubscriberEmail cannot be specified");
            if (model.NewSubscriberEmail is not null && !model.NewSubscriberEmail.IsValidEmail())
                ModelState.AddModelError(nameof(model.NewSubscriberEmail), "Invalid email");
            ValidateMetadata(nameof(model.NewSubscriberMetadata), model.NewSubscriberMetadata);
            ValidateMetadata(nameof(model.InvoiceMetadata), model.InvoiceMetadata);
            ValidateMetadata(nameof(model.Metadata), model.Metadata);
            if (!await CanManageSubscribers(model.StoreId))
                return this.CreateAPIPermissionError(SubscriptionsPolicies.CanManageSubscribers);
            var plan = await ctx.Plans.GetPlanFromId(model.PlanId ?? "", model.OfferingId ?? "", model.StoreId ?? "");
            if (plan is null)
                return PlanNotFound();

            if (model.CreditPurchase is not null)
                model.CreditPurchase = RoundAmount(model.CreditPurchase.Value, plan.Currency);
            if (model.CreditPurchase is <= 0.0m)
                ModelState.AddModelError(nameof(model.CreditPurchase), "CreditPurchase must be greater than 0 or left null");
            if (!ModelState.IsValid || model.PlanId is null || model.OfferingId is null)
                return this.CreateValidationError(ModelState);

            var data = new PlanCheckoutData()
            {
                NewSubscriber = selector is null,
                NewSubscriberEmail = model.NewSubscriberEmail,
                NewSubscriberMetadata = model.NewSubscriberMetadata?.ToString() ?? "{}",
                InvoiceMetadata = model.InvoiceMetadata?.ToString() ?? "{}",
                IsTrial = plan.TrialDays > 0 && model.IsTrial is true,
                PlanId = plan.Id,
                CreditPurchase = model.CreditPurchase,
                SuccessRedirectUrl = Uri.IsWellFormedUriString(model.SuccessRedirectLink, UriKind.Absolute) ? model.SuccessRedirectLink : null,
                Metadata = model.Metadata?.ToString() ?? "{}",
                BaseUrl = Request.GetRequestBaseUrl()
            };
            if (model.OnPayBehavior is { } b)
                data.OnPay = Mapper.Map(b);

            if (selector is not null)
            {
                var sub = await ctx.Subscribers.GetBySelector(model.OfferingId, selector, model.StoreId);
                if (sub is null)
                    return SubscriberNotFound();
                data.SubscriberId = sub.Id;
            }

            if (model.DurationMinutes is { Ticks: > 0 } d)
                data.Expiration = DateTimeOffset.UtcNow + d;
            ctx.PlanCheckouts.Add(data);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetPlanCheckout(data.Id);
        }

        decimal RoundAmount(decimal amount, string currency)
            => Math.Round(amount, currencyNameTable.GetNumberFormatInfo(currency)?.CurrencyDecimalDigits ?? 2);

        private async Task<bool> CanManageSubscribers(string? storeId)
            => (await authorizationService.AuthorizeAsync(User, storeId ?? "???", new PolicyRequirement(SubscriptionsPolicies.CanManageSubscribers))).Succeeded;

        private static void HideMerchantData(SubscriberModel subscriber, IEnumerable<string> otherVisiblePlanIds)
        {
            subscriber.Metadata = null;
            subscriber.Customer.Metadata = null;
            HideMerchantData(subscriber.Plan);
            HideMerchantData(subscriber.NextPlan);
            HideMerchantData(subscriber.ScheduledPlan);

            var visiblePlanIds = otherVisiblePlanIds.Append(subscriber.Plan.Id).Append(subscriber.NextPlan.Id).ToHashSet();
            var offering = subscriber.Offering;
            offering.Metadata = null;
            offering.Plans = offering.Plans?.Where(p => visiblePlanIds.Contains(p.Id)).ToList();
            offering.Plans?.ForEach(HideMerchantData);
            offering.Features = offering.Features?.Where(f => offering.Plans?.Any(p => p.Features.Contains(f.Id)) is true).ToList();
        }

        private static void HideMerchantData(OfferingPlanModel? plan)
        {
            if (plan is null)
                return;
            plan.MemberCount = 0;
            plan.Metadata = null;
        }

        [AllowAnonymous]
        [HttpGet("~/api/v1/subscriber-portal/{portalSessionId}")]
        public async Task<IActionResult> GetPortalSession(string portalSessionId)
        {
            // Uses GetById, not GetActiveById: expired sessions gets cleaned up every month
            var session = await ctx.PortalSessions.GetById(portalSessionId);
            if (session is null)
                return PortalSessionNotFound();
            var model = Mapper.MapPortalSession(session);
            if (!(await CanManageSubscribers(session.GetStoreData().Id)))
                HideMerchantData(model.Subscriber, session.Subscriber.Plan.PlanChanges.Select(p => p.PlanChangeId));
            return Ok(model);
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/subscriber-portal")]
        public async Task<IActionResult> CreatePortalSession([FromBody]CreatePortalSessionRequest model)
        {
            if (model.StoreId is null)
                ModelState.AddModelError(nameof(model.StoreId), "StoreId is required");
            if (model.OfferingId is null)
                ModelState.AddModelError(nameof(model.OfferingId), "OfferingId is required");
            var selector = GetSelector(nameof(model.CustomerSelector), model.CustomerSelector, false);
            if (selector is null || model.OfferingId is null || !ModelState.IsValid || model.StoreId is null)
                return this.CreateValidationError(ModelState);
            if (!await CanManageSubscribers(model.StoreId))
                return this.CreateAPIPermissionError(SubscriptionsPolicies.CanManageSubscribers);
            var sub = await ctx.Subscribers.GetBySelector(model.OfferingId, selector, model.StoreId);
            if (sub is null)
                return SubscriberNotFound();

            var data = new PortalSessionData()
            {
                SubscriberId = sub.Id,
                BaseUrl = Request.GetRequestBaseUrl()
            };
            if (model.DurationMinutes is { Ticks: > 0 } d)
                data.Expiration = DateTimeOffset.UtcNow + d;
            ctx.PortalSessions.Add(data);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            return await GetPortalSession(data.Id);
        }

        private CustomerSelector? GetSelector(string fieldName, string? selectorString, bool required = true)
        {
            if (string.IsNullOrEmpty(selectorString))
            {
                if (required)
                    ModelState.AddModelError(fieldName, "CustomerSelector is required");
                return null;
            }
            if (CustomerSelector.TryParse(selectorString, out var selector))
                return selector;
            ModelState.AddModelError(fieldName, CustomerSelectorModelBinder.InvalidFormat);
            return null;
        }

        private void ValidateMetadata(string fieldName, JObject? metadata)
        {
            if (metadata?.ToString().Length > MaxMetadataLength)
                ModelState.AddModelError(fieldName, "Metadata is too large");
        }

        private IActionResult PortalSessionNotFound()
            => this.CreateAPIError(404, "portal-session-not-found", "The portal session was not found");

        private IActionResult CheckoutNotFound()
            => this.CreateAPIError(404, "checkout-plan-not-found", "The checkout plan was not found");
        private IActionResult CheckoutExpired()
            => this.CreateAPIError(404, "checkout-plan-expired", "The checkout plan is expired");

        private IActionResult SubscriberNotFound()
            => this.CreateAPIError(404, "subscriber-not-found", "The subscriber was not found");

        private IActionResult OfferingNotFound()
            => this.CreateAPIError(404, "offering-not-found", "The offering was not found");
        private IActionResult PlanNotFound()
            => this.CreateAPIError(404, "plan-not-found", "The plan was not found");
    }
}
