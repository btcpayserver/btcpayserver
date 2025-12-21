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
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace BTCPayServer.Plugins.Emails.HostedServices;

public class UserEventHostedService(
    EventAggregator eventAggregator,
    IServiceScopeFactory serviceScopeFactory,
    NotificationSender notificationSender,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
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
        return $"<img src='data:image/png;base64,{base64}' alt='{HtmlEncoder.Default.Encode(data)}' width='320' height='320'/>";
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
                    {
                        var trigger = await CreateTriggerEvent(ServerMailTriggers.InvitePending,
                            new JObject()
                            {
                                ["InvitationLink"] = invited.InvitationLink,
                                ["InvitationLinkQR"] = GetQrCodeImg(invited.InvitationLink)
                            }, user);
                        trigger.RawHtml.Add("InvitationLinkQR");
                        EventAggregator.Publish(trigger);
                    }
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
                        ["ConfirmLink"] = confReq.ConfirmLink
                    }, user));
                break;

            case UserEvent.PasswordResetRequested pwResetEvent:
                EventAggregator.Publish(await CreateTriggerEvent(ServerMailTriggers.PasswordReset,
                    new JObject()
                    {
                        ["ResetLink"] = pwResetEvent.ResetLink
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
        using var scope = serviceScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
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
         var evt = new TriggerEvent(null, trigger, model, null);
        return evt;
    }
}
