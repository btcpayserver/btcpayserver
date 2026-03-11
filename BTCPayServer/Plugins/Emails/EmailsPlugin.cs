using System.Collections.Generic;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Emails;

public class EmailsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Emails";
    public override string Identifier => "BTCPayServer.Plugins.Emails";
    public override string Name => "Emails";
    public override string Description => "Allows you to send emails to your customers!";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IDefaultTranslationProvider, EmailsTranslationProvider>();
        services.AddSingleton<IHostedService, StoreEmailRuleProcessorSender>();
        services.AddTransient<EmailTriggerViewModels>();
        services.AddSingleton<IHostedService, UserEventHostedService>();
        services.AddMigration<ApplicationDbContext, Migrations.DefaultServerEmailRulesMigration>();
        services.AddMigration<ApplicationDbContext, Migrations.ServerEmailSettingsMigration>();

        services.AddSingleton<IEmailTriggerViewModelTransformer, ServerTransformer>();
        services.AddSingleton<IEmailTriggerEventTransformer, ServerTransformer>();
        services.AddDefaultTranslations(ServerTransformer.TranslatedStrings);

        services.AddSingleton<IEmailTriggerViewModelTransformer, StoreTransformer>();
        services.AddSingleton<IEmailTriggerEventTransformer, StoreTransformer>();
        services.AddDefaultTranslations(StoreTransformer.TranslatedStrings);


        RegisterServerEmailTriggers(services);
    }
    private static string BODY_STYLE = "font-family: Open Sans, Helvetica Neue,Arial,sans-serif; font-color: #292929;";
    private static string HEADER_HTML = "<h1 style='font-size:1.2rem'>{Server.Name}</h1><br/>";
    private static string BUTTON_HTML = "<a href='{button_link}' type='submit' style='min-width: 2em;min-height: 20px;text-decoration-line: none;cursor: pointer;display: inline-block;font-weight: 400;color: #fff;text-align: center;vertical-align: middle;user-select: none;background-color: #51b13e;border-color: #51b13e;border: 1px solid transparent;padding: 0.375rem 0.75rem;font-size: 1rem;line-height: 1.5;border-radius: 0.25rem;transition: color 0.15s ease-in-out, background-color 0.15s ease-in-out, border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;'>{button_description}</a>";

    public static string CallToAction(string actionName, string actionLink)
    {
        var button = $"{BUTTON_HTML}".Replace("{button_description}", actionName, System.StringComparison.InvariantCulture);
        return button.Replace("{button_link}", actionLink, System.StringComparison.InvariantCulture);
    }

    public static string CreateEmailBody(string body) => $"<html><body style='{BODY_STYLE}'>{HEADER_HTML}{body}</body></html>";

    public static string CreateEmail(string body, string actionName = null,  string actionLink = null)
    {
        if (actionName is null || actionLink is null)
            return CreateEmailBody(body);
        return CreateEmailBody($"{body}<br/><br/>{CallToAction(actionName, actionLink)}");
    }

    private void RegisterServerEmailTriggers(IServiceCollection services)
    {

        List<EmailTriggerViewModel> vms = new();

        var vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.PasswordReset,
            DefaultEmail = new()
            {
                To = ["{User.MailboxAddress}"],
                Subject = "Update Password",
                Body = CreateEmailBody($"A request has been made to reset your {{Server.Name}} password. Please set your password by clicking below.<br/><br/>{CallToAction("Update Password", "{ResetLink}")}"),
            },
            PlaceHolders = new()
            {
                new ("{ResetLink}", "The link to the password reset page")
            },
            Description = "User: Password Reset Requested",
        };
        vms.Add(vm);

        vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.EmailConfirm,
            DefaultEmail = new()
            {
                To = ["{User.MailboxAddress}"],
                Subject = "Confirm your email address",
                Body = CreateEmailBody($"Please confirm your account.<br/><br/>{CallToAction("Confirm Email", "{ConfirmLink}")}"),
            },
            PlaceHolders = new()
            {
                new ("{ConfirmLink}", "The link used to confirm the user's email address")
            },
            Description = "User: Email confirmation",
        };
        vms.Add(vm);

        vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.InvitePending,
            DefaultEmail = new()
            {
                To = ["{User.MailboxAddress}"],
                Subject = "Invitation to join {Server.Name}",
                Body = CreateEmailBody($"<p>Please complete your account setup by clicking <a href='{{InvitationLink}}'>this link</a>.</p><p>You can also use the BTCPay Server app and scan this QR code when connecting:</p>{{InvitationLinkQR}}"),
            },
            PlaceHolders = new()
            {
                new ("{InvitationLink}", "The link where the invited user can set up their account"),
                new ("{InvitationLinkQR}", "The QR code representation of the invitation link")
            },
            Description = "User: Invitation",
        };
        vms.Add(vm);

        vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.ApprovalConfirmed,
            DefaultEmail = new()
            {
                To = ["{User.MailboxAddress}"],
                Subject = "Your account has been approved",
                Body = CreateEmailBody($"Your account has been approved and you can now log in.<br/><br/>{CallToAction("Login here", "{LoginLink}")}"),
            },
            PlaceHolders = new()
            {
                new ("{LoginLink}", "The link that the user can use to login"),
            },
            Description = "User: Account approved",
        };
        vms.Add(vm);

        vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.ApprovalRequest,
            DefaultEmail = new()
            {
                To = ["{Admins.MailboxAddresses}"],
                Subject = "Approval request to access the server for {User.Email}",
                Body = CreateEmailBody($"A new user ({{User.MailboxAddress}}), is awaiting approval to access the server.<br/><br/>{CallToAction("Approve", "{ApprovalLink}")}"),
            },
            PlaceHolders = new()
            {
                new ("{ApprovalLink}", "The link that the admin needs to use to approve the user"),
            },
            Description = "Admin: Approval request",
        };
        vms.Add(vm);

        var commonPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{Admins.MailboxAddresses}", "The email addresses of the admins separated by a comma"),
            new("{User.Name}", "The name of the user (eg. John Doe)"),
            new("{User.Email}", "The email of the user (eg. john.doe@example.com)"),
            new("{User.MailboxAddress}", "The formatted mailbox address to use when sending an email. (eg. \"John Doe\" <john.doe@example.com>)")
        };
        foreach (var v in vms)
        {
            v.ServerTrigger = true;
            v.PlaceHolders.InsertRange(0, commonPlaceholders);
            services.AddSingleton(v);
        }
    }
}
