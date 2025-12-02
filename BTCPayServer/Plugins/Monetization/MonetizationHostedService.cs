#nullable enable
using System;
using System.Collections.Generic;
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
    public class MonetizationLockoutUpdated((string UserId, bool LockoutEnabled)[] updated)
    {
        public (string UserId, bool LockoutEnabled)[] Updated { get; } = updated;
    }

    protected override void SubscribeToEvents()
    {
        this.Subscribe<SubscriptionEvent.NewSubscriber>();
        this.Subscribe<SubscriptionEvent.SubscriberActivated>();
        this.Subscribe<SubscriptionEvent.SubscriberDisabled>();
        this.Subscribe<SubscriptionEvent.PlanUpdated>();
        this.Subscribe<SubscriptionEvent.PlanStarted>();
        this.SubscribeAny<UserEvent.Registered>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is SubscriptionEvent.SubscriberEvent se && !IsMonetization(se))
            return;
        using var scope = serviceScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (evt is SubscriptionEvent.NewSubscriber newSub && newSub.Subscriber.GetApplicationUserId() is null)
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
                await AttachUserIdToSubscriber(user.Id, newSub);
                var callbackGenerator = scope.ServiceProvider.GetRequiredService<CallbackGenerator>();
                callbackGenerator.BaseUrl = newSub.RequestBaseUrl;
                EventAggregator.Publish(await UserEvent.Registered.Create(user, null, callbackGenerator));
            }
            else
            {
                var existing = await userManager.FindByEmailAsync(email ?? "");
                if (existing is not null)
                    await AttachUserIdToSubscriber(existing.Id, newSub);
            }
        }

        if (evt is SubscriptionEvent.SubscriberActivated or SubscriptionEvent.SubscriberDisabled)
        {
            await UpdateUserLockout((SubscriptionEvent.SubscriberEvent)evt, userManager, evt is SubscriptionEvent.SubscriberActivated);
        }

        if (evt is SubscriptionEvent.PlanStarted ps && ps.PreviousPlan.Id != ps.Subscriber.Plan.Id)
        {
            await using var ctx = dbContextFactory.CreateContext();
            var canAccess = await ctx.Plans.HasFeature(ps.Subscriber.PlanId, MonetizationFeatures.CanAccess);
            await UpdateUserLockout(ps, userManager, canAccess);
        }

        if (evt is SubscriptionEvent.PlanUpdated pu)
        {
            await using var ctx = dbContextFactory.CreateContext();
            await UpdateUserLockoutStatus(ctx, pu.Plan);
        }

        if (evt is UserEvent.Registered reg && monetizationSettingsAccessor.Settings is
            {
                OfferingId: { } offeringId,
                DefaultPlanId: { } defaultPlanId
            })
        {
            if (await userService.IsAdminUser(reg.User))
                return;
            await using var ctx = dbContextFactory.CreateContext();
            var userSub = await ctx.Subscribers.GetBySelector(offeringId, CustomerSelector.ByIdentity(SubscriberDataExtensions.IdentityType, reg.User.Id));
            if (userSub is not null)
                return;
            var inserted = (await MigrateUsers(offeringId, defaultPlanId, OneUserQuery, parameters =>
            {
                parameters.Add("userId", reg.User.Id);
                parameters.Add("email", reg.User.Email);
                parameters.Add("customerId", CustomerData.GenerateId());
            }));
            if (inserted.Length == 1)
            {
                var s = await ctx.Subscribers.GetByCustomerId(inserted[0].CustomerId, offeringId);
                if (s is not null)
                    EventAggregator.Publish(new SubscriptionEvent.NewSubscriber(s, reg.RequestBaseUrl));
            }
        }
    }

    private async Task UpdateUserLockout(SubscriptionEvent.SubscriberEvent evt, UserManager<ApplicationUser> userManager, bool activated)
    {
        var userId = evt.Subscriber.GetApplicationUserId();
        var user = await userManager.FindByIdAsync(userId ?? "");
        if (user is not null &&
            await userService.SetDisabled(user.Id, !activated) is not UserService.SetDisabledResult.Error)
            EventAggregator.Publish(new MonetizationLockoutUpdated([(user.Id, !activated)]));
    }

    private async Task AttachUserIdToSubscriber(string id, SubscriptionEvent.NewSubscriber newSub)
    {
        await using var ctx = dbContextFactory.CreateContext();
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          INSERT INTO customers_identities (customer_id, type, value) VALUES (@customerId, @identityType, @userId)
                          ON CONFLICT (customer_id, type) DO NOTHING;
                          """, new { customerId = newSub.Subscriber.CustomerId, userId = id, identityType = SubscriberDataExtensions.IdentityType });
    }

    private bool IsMonetization(SubscriptionEvent.SubscriberEvent se)
        => monetizationSettingsAccessor.Settings.OfferingId == se.Subscriber.OfferingId;


    private const string NonAdminUserQuery = """
                                             WITH subs AS (
                                                 SELECT s.id, ci.value user_id
                                                 FROM subs_subscribers s
                                                 JOIN customers_identities ci ON ci.customer_id = s.customer_id
                                                 WHERE s.offering_id = @offeringId AND ci.type = @applicationUserId
                                             ),
                                             non_admin_users AS (
                                                 SELECT DISTINCT u."Id" user_id, u."Email" email
                                                 FROM "AspNetUsers" u
                                                       LEFT JOIN "AspNetUserRoles" ur ON u."Id" = ur."UserId"
                                                       LEFT JOIN "AspNetRoles" r ON r."Name"=@adminRole AND ur."RoleId" = r."Id"
                                                 WHERE r."Id" IS NULL
                                             ),
                                             users_to_migrate AS (
                                                 SELECT u.user_id, u.email, NULL as customer_id
                                                 FROM non_admin_users u
                                                 LEFT JOIN subs s ON u.user_id = s.user_id
                                                 WHERE s.user_id IS NULL
                                             )
                                             """;

    private const string OneUserQuery = """
                                        WITH users_to_migrate AS (
                                            SELECT @userId as user_id, @email as email, @customerId as customer_id
                                        )
                                        """;

    public async Task<(string CustomerId, string UserId)[]> MigrateUsers(string? offeringId, string? planId, string usersQuery = NonAdminUserQuery, Action<DynamicParameters>? addParameters = null)
    {
        if (offeringId is null || planId is null)
            return Array.Empty<(string CustomerId, string UserId)>();
        await using var ctx = dbContextFactory.CreateContext();
        if (await ctx.Offerings.GetOfferingData(offeringId) is not { } offering ||
            offering.Plans
                .Where(p => p.Status == PlanData.PlanStatus.Active)
                .FirstOrDefault(p => p.Id == planId) is not { } plan)
            return Array.Empty<(string CustomerId, string UserId)>();

        var hasTrial = plan.TrialDays > 0;
        var dummy = new SubscriberData();
        dummy.Plan = plan;
        if (hasTrial)
            dummy.StartNextPlan(DateTimeOffset.UtcNow, hasTrial);

        DynamicParameters parameters = new(new
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
            phase = (hasTrial ? SubscriberData.PhaseTypes.Trial : SubscriberData.PhaseTypes.Expired).ToString(),
            active = hasTrial,
            applicationUserId = SubscriberDataExtensions.IdentityType,
            adminRole = Roles.ServerAdmin
        });
        addParameters?.Invoke(parameters);
        var userIds = (await ctx.Database.GetDbConnection()
            .QueryAsync<(string CustomerId, string UserId)>($"""
                                {usersQuery},
                                customers_already_created AS (
                                    SELECT c.id AS customer_id, um.email, um.user_id  FROM users_to_migrate um
                                    JOIN customers_identities ci ON ci.type = 'Email' AND ci.value = um.email
                                    JOIN customers c ON c.id = ci.customer_id AND c.store_id = @storeId
                                ),
                                customers_to_create AS (
                                    SELECT COALESCE(um.customer_id, 'cust_' || translate(encode(decode(md5(um.email || @salt), 'hex'), 'base64'), '/=+-', '')) AS customer_id, um.email, um.user_id FROM users_to_migrate um
                                        LEFT JOIN customers_already_created cac ON um.user_id = cac.user_id
                                    WHERE cac.user_id IS NULL
                                ),
                                customers_all AS (
                                      SELECT * FROM customers_to_create UNION ALL SELECT * FROM customers_already_created
                                ),
                                inserted_customers AS (
                                    INSERT INTO customers (id, store_id)
                                        SELECT customer_id, @storeId
                                        FROM customers_to_create cc
                                    RETURNING id, store_id
                                ),
                                inserted_email_identities AS (
                                    INSERT INTO customers_identities (customer_id, type, value)
                                           SELECT customer_id, 'Email', email FROM customers_to_create
                                        RETURNING customer_id, type, value
                                ),
                                inserted_userId_identities AS (
                                    INSERT INTO customers_identities (customer_id, type, value)
                                           SELECT customer_id, @applicationUserId, user_id FROM customers_all
                                          ON CONFLICT DO NOTHING
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
                                    optimistic_activation)
                                    SELECT um.customer_id,
                                           @offeringId,
                                           @planId,
                                           @planStarted,
                                           @trialEnd,
                                           @periodEnd,
                                           @gracePeriodEnd,
                                           @paidAmount,
                                           @phase,
                                           @active,
                                           false
                                           FROM customers_all um
                                    ON CONFLICT (customer_id, offering_id) DO NOTHING
                                    RETURNING customer_id
                                )
                                SELECT i.customer_id, COALESCE(u.value, ci.value) user_id
                                FROM inserted_subs i
                                LEFT JOIN customers_identities ci ON ci.type = @applicationUserId AND ci.customer_id = i.customer_id
                                LEFT JOIN inserted_userId_identities u ON u.customer_id = i.customer_id;
                                """, parameters)).ToArray();
        if (userIds.Length != 0)
        {
            await SubscriptionHostedService.UpdatePlanStats(ctx, plan.Id);
            await UpdateUserLockoutStatus(ctx, plan, userIds.Select(c => c.UserId).ToArray());
            // We expect the caller to call NewSubscriberEvent.
        }

        return userIds;
    }

    private async Task UpdateUserLockoutStatus(ApplicationDbContext ctx, PlanData plan, string[]? userIds = null)
    {
        if (userIds is null)
            userIds = await GetUserIdsInPlan(ctx, plan);
        if (userIds.Length == 0)
            return;
        var canAccess = await ctx.Plans.HasFeature(plan.Id, MonetizationFeatures.CanAccess);
        var updated = (await ctx.Database.GetDbConnection()
            .QueryAsync<(string UserId, bool LockoutEnabled)>("""
                          WITH
                          subs AS (
                              SELECT user_id AS user_id,
                                     NOT (ss.active AND @canAccess) AS lockout_enabled,
                                     CASE WHEN (ss.active AND @canAccess) THEN NULL ELSE 'infinity'::timestamptz END lockout_end
                              FROM unnest(@userIds) AS user_id
                                       JOIN customers_identities ci ON ci.type = @applicationUserId AND ci.value = user_id
                                       JOIN subs_subscribers ss ON ss.customer_id = ci.customer_id
                                       JOIN "AspNetUsers" ON "AspNetUsers"."Id" = user_id
                              WHERE ss.plan_id = @planId
                          )
                          UPDATE "AspNetUsers"
                          SET "LockoutEnabled" = subs.lockout_enabled,
                              "LockoutEnd" = subs.lockout_end,
                              "SecurityStamp" = translate(encode(decode(md5("Id" || @salt), 'hex'), 'base64'), '/=+-', '')
                          FROM subs WHERE "AspNetUsers"."Id" = subs.user_id AND ("AspNetUsers"."LockoutEnabled" IS DISTINCT FROM subs.lockout_enabled OR "AspNetUsers"."LockoutEnd" IS DISTINCT FROM subs.lockout_end)
                          RETURNING "Id", "LockoutEnabled";
                          """,
                new{
                    userIds,
                    planId = plan.Id,
                    salt = RandomUtils.GetUInt256().ToString(),
                    applicationUserId = SubscriberDataExtensions.IdentityType,
                    canAccess
                })).ToArray();
        foreach (var update in updated)
            if (update.LockoutEnabled)
                disabledUsers.Add(update.UserId);
            else
                disabledUsers.Remove(update.UserId);
        EventAggregator.Publish(new MonetizationLockoutUpdated(updated));
    }

    private static async Task<string[]> GetUserIdsInPlan(ApplicationDbContext ctx, PlanData plan)
        => (await ctx.Database.GetDbConnection()
                .QueryAsync<string>("""
                                    SELECT ci.value
                                    FROM subs_subscribers s
                                    JOIN customers_identities ci ON ci.customer_id = s.customer_id
                                    WHERE s.offering_id=@offeringId AND s.plan_id = @planId AND ci.type = @applicationUserId
                                    """, new { planId = plan.Id, offeringId = plan.OfferingId, applicationUserId = SubscriberDataExtensions.IdentityType }))
            .ToArray();
}
