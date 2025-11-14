#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationHostedService(
    ApplicationDbContextFactory dbContextFactory,
    EventAggregator eventAggregator,
    SettingsRepository settingsRepository,
    UserService userService,
    BTCPayServerSecurityStampValidator.DisabledUsers disabledUsers,
    ISettingsAccessor<MonetizationSettings> monetizationSettingsAccessor,
    IServiceScopeFactory serviceScopeFactory,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        this.Subscribe<SubscriptionEvent.NewSubscriber>();
        this.Subscribe<SubscriptionEvent.SubscriberActivated>();
        this.Subscribe<SubscriptionEvent.SubscriberDisabled>();
        this.Subscribe<SubscriptionEvent.PlanUpdated>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is SubscriptionEvent.SubscriberEvent se && !IsMonetization(se))
            return;
        using var scope = serviceScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (evt is SubscriptionEvent.NewSubscriber newSub)
        {
            var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var email = newSub.Subscriber.Customer.Email.Get();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                RequiresApproval = policies.RequiresUserApproval,
                Created = DateTimeOffset.UtcNow,
                Approved = false
            };
            var created = await userManager.CreateAsync(user);
            if (created.Succeeded)
            {
                await AttachUserIdToSubscriber(user, newSub);
                var callbackGenerator = scope.ServiceProvider.GetRequiredService<CallbackGenerator>();
                callbackGenerator.BaseUrl = newSub.Checkout.BaseUrl;
                var invited = await UserEvent.Invited.Create(user, user, callbackGenerator, true);
                EventAggregator.Publish(invited);
            }
        }

        if (evt is (SubscriptionEvent.SubscriberActivated or SubscriptionEvent.SubscriberDisabled)
            and SubscriptionEvent.SubscriberEvent { Subscriber: {} sub })
        {
            var userId = sub.GetMonetizationData()?.UserId;
            var user = await userManager.FindByIdAsync(userId ?? "");
            var activated = evt is SubscriptionEvent.SubscriberActivated;
            if (user is not null)
            {
                if (activated)
                {
                    await userService.SetDisabled(user.Id, false);
                }
                else
                {
                    await userService.SetDisabled(user.Id, true);
                }
            }
        }

        if (evt is SubscriptionEvent.PlanUpdated pu)
        {
            await using var ctx = dbContextFactory.CreateContext();
            await UpdateUserLockoutStatus(ctx, pu.Plan);
        }
    }

    private async Task AttachUserIdToSubscriber(ApplicationUser user, SubscriptionEvent.NewSubscriber newSub)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var json = BaseEntityData.ToUpdateAdditionalDataJson(SubscriberMonetizationAdditionalData.Key, new SubscriberMonetizationAdditionalData()
        {
            UserId = user.Id
        });
        await ctx.Database.ExecuteSqlAsync($"UPDATE subs_subscribers SET additional_data = additional_data || {json}::JSONB WHERE Id = {newSub.Subscriber.Id}");
    }

    private bool IsMonetization(SubscriptionEvent.SubscriberEvent se)
        => monetizationSettingsAccessor.Settings.OfferingId == se.Subscriber.OfferingId;

    public async Task<int> MigrateUsers(string? offeringId, string? planId)
    {
        if (offeringId is null || planId is null)
            return 0;
        await using var ctx = dbContextFactory.CreateContext();
        if (await ctx.Offerings.GetOfferingData(offeringId) is not { } offering ||
            offering.Plans
                .Where(p => p.Status == PlanData.PlanStatus.Active)
                .FirstOrDefault(p => p.Id == planId) is not { } plan)
            return 0;

        var hasTrial = plan.TrialDays > 0;
        var dummy = new SubscriberData();
        dummy.Plan = plan;
        dummy.StartNextPlan(DateTimeOffset.UtcNow, hasTrial);
        var userIds = (await ctx.Database.GetDbConnection()
            .QueryAsync<string>($"""
                          WITH subs AS (
                              SELECT id, additional_data->'{SubscriberMonetizationAdditionalData.Key}'->>'userId' user_id
                              FROM subs_subscribers
                              WHERE offering_id = @offeringId
                          ),
                          non_admin_users AS (
                              SELECT DISTINCT u."Id" user_id, u."Email" email
                              FROM "AspNetUsers" u
                                    LEFT JOIN "AspNetUserRoles" ur ON u."Id" = ur."UserId"
                                    LEFT JOIN "AspNetRoles" r ON r."Name"='{Roles.ServerAdmin}' AND ur."RoleId" = r."Id"
                              WHERE r."Id" IS NULL
                          ),
                          users_to_migrate AS (
                              SELECT u.user_id, u.email
                              FROM non_admin_users u
                              LEFT JOIN subs s ON u.user_id = s.user_id
                              WHERE s.user_id IS NULL
                          ),
                          customers_to_create AS (
                              SELECT 'cust_' || translate(encode(decode(md5(um.email || @salt), 'hex'), 'base64'), '/=+-', '') AS customer_id, um.email, um.user_id FROM users_to_migrate um
                              LEFT JOIN customers_identities ci ON ci.type = 'Email' AND ci.value = um.email
                              LEFT JOIN customers c ON c.id = ci.customer_id AND c.store_id = @storeId
                              WHERE c.id IS NULL
                          ),
                          customers_already_created AS (
                              SELECT c.id, um.email, um.user_id  FROM users_to_migrate um
                              JOIN customers_identities ci ON ci.type = 'Email' AND ci.value = um.email
                              JOIN customers c ON c.id = ci.customer_id AND c.store_id = @storeId
                          ),
                          inserted_customers AS (
                              INSERT INTO customers (id, store_id)
                                  SELECT customer_id, @storeId
                                  FROM customers_to_create cc
                              RETURNING id, store_id
                          ),
                          inserted_identities AS (
                              INSERT INTO customers_identities (customer_id, type, value)
                                     SELECT customer_id, 'Email', email FROM customers_to_create
                                  RETURNING customer_id, type, value
                          ),
                          inserted_subs AS (
                              INSERT INTO subs_subscribers (
                              customer_id,
                              offering_id,
                              plan_id,
                              plan_started,
                              trial_end,
                              period_end,
                              grace_period_end,
                              paid_amount,
                              phase,
                              active,
                              optimistic_activation,
                              additional_data)
                              SELECT um.customer_id,
                                     @offeringId,
                                     @planId,
                                     @planStarted,
                                     @trialEnd,
                                     @periodEnd,
                                     @gracePeriodEnd,
                                     @paidAmount,
                                     @phase,
                                     true,
                                     false,
                                     jsonb_build_object('{SubscriberMonetizationAdditionalData.Key}',jsonb_build_object('userId', um.user_id))
                                     FROM (SELECT * FROM customers_to_create UNION ALL SELECT * FROM customers_already_created) um
                              ON CONFLICT (customer_id, offering_id) DO NOTHING
                              RETURNING additional_data->'{SubscriberMonetizationAdditionalData.Key}'->>'userId' user_id
                          )
                          SELECT user_id FROM inserted_subs;
                          """, new
            {
                offeringId,
                planId,
                storeId = offering.App.StoreDataId,
                salt = RandomUtils.GetUInt256().ToString(),
                planStarted = dummy.PlanStarted,
                trialEnd = dummy.TrialEnd,
                periodEnd = dummy.PeriodEnd,
                gracePeriodEnd = dummy.GracePeriodEnd,
                paidAmount = dummy.PaidAmount,
                phase = (hasTrial ? SubscriberData.PhaseTypes.Trial : SubscriberData.PhaseTypes.Normal).ToString()
            })).ToArray();
        if (userIds.Length != 0)
        {
            await SubscriptionHostedService.UpdatePlanStats(ctx, plan.Id);
            await UpdateUserLockoutStatus(ctx, plan, userIds);
        }
        return userIds.Length;
    }

    private async Task UpdateUserLockoutStatus(ApplicationDbContext ctx, PlanData plan, string[]? userIds = null)
    {
        if (userIds is null)
            userIds = await GetUserIdsInPlan(ctx, plan);
        if (userIds.Length == 0)
            return;
        await plan.EnsureEntitlmentLoaded(ctx);
        var canLogin = plan.PlanEntitlements.Any(pe => pe.Entitlement.CustomId == MonetizationEntitlments.CanLogin);
        var lockoutEnabled = !canLogin;
        DateTimeOffset? lockoutDate = canLogin ? null : DateTimeOffset.MaxValue;
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE "AspNetUsers" SET "LockoutEnabled" = @lockoutEnabled,
                                                   "LockoutEnd" = @lockoutDate,
                                                   "SecurityStamp" = translate(encode(decode(md5("Id" || @salt), 'hex'), 'base64'), '/=+-', '')
                          WHERE "Id" = ANY(@userIds) AND ("LockoutEnabled" IS DISTINCT FROM @lockoutEnabled OR "LockoutEnd" IS DISTINCT FROM @lockoutDate);
                          """, new{ userIds, lockoutEnabled, lockoutDate, salt = RandomUtils.GetUInt256().ToString() });
        foreach (var userId in userIds)
            if (canLogin)
                disabledUsers.Remove(userId);
            else
                disabledUsers.Add(userId);
    }

    private static async Task<string[]> GetUserIdsInPlan(ApplicationDbContext ctx, PlanData plan)
    => (await ctx.Database.GetDbConnection()
        .QueryAsync<string>($"""
                             SELECT additional_data->'{SubscriberMonetizationAdditionalData.Key}'->>'userId'
                             FROM subs_subscribers
                             WHERE offering_id=@offeringId AND plan_id = @planId
                             """, new { planId = plan.Id, offeringId = plan.OfferingId })).ToArray();
}
