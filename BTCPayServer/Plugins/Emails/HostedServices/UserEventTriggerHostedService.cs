#nullable enable
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace BTCPayServer.Plugins.Emails.HostedServices;

public class UserEventHostedService(
    EventAggregator eventAggregator,
    UserManager<ApplicationUser> userManager,
    ISettingsAccessor<ServerSettings> serverSettings,
    EmailSenderFactory emailSenderFactory,
    NotificationSender notificationSender,
    StoreRepository storeRepository,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    public UserManager<ApplicationUser> UserManager { get; } = userManager;

    protected override void SubscribeToEvents()
    {
        SubscribeAny<UserEvent>();
    }

    public static string GetQrCodeImg(string data)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new Base64QRCode(qrCodeData);
        var base64 = qrCode.GetGraphic(20);
        return $"<img src='data:image/png;base64,{base64}' alt='{data}' width='320' height='320'/>";
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        var user = (evt as UserEvent)?.User;
        if (user is null) return;
        switch (evt)
        {
            case UserEvent.Registered ev:
                var requiresApproval = user is { RequiresApproval: true, Approved: false };
                var requiresEmailConfirmation = user is { RequiresEmailConfirmation: true, EmailConfirmed: false };

                // send notification if the user does not require email confirmation.
                // inform admins only about qualified users and not annoy them with bot registrations.
                if (requiresApproval && !requiresEmailConfirmation)
                {
                    await NotifyAdminsAboutUserRequiringApproval(user, ev.ApprovalLink);
                }

                // set callback result and send email to user
                if (ev is UserEvent.Invited invited)
                {
                    if (invited.SendInvitationEmail)
                        EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.InvitePending,
                            new JObject()
                            {
                                ["InvitationLink"] = HtmlEncoder.Default.Encode(invited.InvitationLink),
                                ["InvitationLinkQR"] = GetQrCodeImg(invited.InvitationLink)
                            }, user));
                }
                else if (requiresEmailConfirmation)
                {
                    EventAggregator.Publish(new UserEvent.ConfirmationEmailRequested(user, ev.ConfirmationEmailLink));
                }
                break;
            case UserEvent.ConfirmationEmailRequested confReq:
                EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.EmailConfirm,
                    new JObject()
                    {
                        ["ConfirmLink"] = HtmlEncoder.Default.Encode(confReq.ConfirmLink)
                    }, user));
                break;

            case UserEvent.PasswordResetRequested pwResetEvent:
                EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.PasswordReset,
                    new JObject()
                    {
                        ["ResetLink"] = HtmlEncoder.Default.Encode(pwResetEvent.ResetLink)
                    }, user));
                break;

            case UserEvent.Approved approvedEvent:
                if (!user.Approved) break;
                EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.ApprovalConfirmed,
                    new JObject()
                    {
                        ["LoginLink"] = approvedEvent.LoginLink
                    }, user));

                break;

            case UserEvent.ConfirmedEmail confirmedEvent when user is { RequiresApproval: true, Approved: false, EmailConfirmed: true }:
                await NotifyAdminsAboutUserRequiringApproval(user, confirmedEvent.ApprovalLink);
                break;

            case UserEvent.InviteAccepted inviteAcceptedEvent:
                Logs.PayServer.LogInformation("User {Email} accepted the invite", user.Email);
                await NotifyAboutUserAcceptingInvite(user, inviteAcceptedEvent.StoreUsersLink);
                break;
        }
    }

    private async Task NotifyAdminsAboutUserRequiringApproval(ApplicationUser user, string approvalLink)
    {
        await notificationSender.SendNotification(new AdminScope(), new NewUserRequiresApprovalNotification(user));
        EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.ApprovalRequest,
            new JObject()
            {
                ["ApprovalLink"] = approvalLink
            }, user));
    }

    private async Task<TriggerEvent> CreateTriggerEvent(string trigger, JObject model, ApplicationUser user)
    {
        var admins = await UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
        var adminMailboxes = string.Join(", ", admins.Select(a => a.GetMailboxAddress().ToString()).ToArray());
        model["Admins"] = new JObject()
        {
            ["MailboxAddresses"] = adminMailboxes,
        };
        model["User"] = new JObject()
        {
            ["Name"] = user.UserName,
            ["Email"] = user.Email,
            ["MailboxAddress"] = user.GetMailboxAddress().ToString(),
        };
        model["Branding"] = new JObject()
        {
            ["ServerName"] = serverSettings.Settings.ServerName ?? "BTCPay Server",
            ["ContactUrl"] = serverSettings.Settings.ContactUrl,
        };
         var evt = new TriggerEvent(null, trigger, model, null);
        return evt;
    }
    private async Task NotifyAboutUserAcceptingInvite(ApplicationUser user, string storeUsersLink)
    {
        var stores = await storeRepository.GetStoresByUserId(user.Id);
        var notifyRoles = new[] { StoreRoleId.Owner, StoreRoleId.Manager };
        foreach (var store in stores)
        {
            // notification
            await notificationSender.SendNotification(new StoreScope(store.Id, notifyRoles), new InviteAcceptedNotification(user, store));
            // email
            var notifyUsers = await storeRepository.GetStoreUsers(store.Id, notifyRoles);
            var link = string.Format(storeUsersLink, store.Id);
            var emailSender = await emailSenderFactory.GetEmailSender(store.Id);
            foreach (var storeUser in notifyUsers)
            {
                if (storeUser.Id == user.Id) continue; // do not notify the user itself (if they were added as owner or manager)
                var notifyUser = await UserManager.FindByIdOrEmail(storeUser.Id);
                var info = $"User {user.Email} accepted the invite to {store.StoreName}";
                emailSender.SendUserInviteAcceptedInfo(notifyUser.GetMailboxAddress(), info, link);
            }
        }
    }
}
