using System.Collections.Generic;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Webhooks;
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
        RegisterServerEmailTriggers(services);
    }
    private static string BODY_STYLE = "font-family: Open Sans, Helvetica Neue,Arial,sans-serif; font-color: #292929;";
    private static string HEADER_HTML = "<h1 style='font-size:1.2rem'>{Branding.ServerName}</h1><br/>";
    private static string BUTTON_HTML = "<a href='{button_link}' type='submit' style='min-width: 2em;min-height: 20px;text-decoration-line: none;cursor: pointer;display: inline-block;font-weight: 400;color: #fff;text-align: center;vertical-align: middle;user-select: none;background-color: #51b13e;border-color: #51b13e;border: 1px solid transparent;padding: 0.375rem 0.75rem;font-size: 1rem;line-height: 1.5;border-radius: 0.25rem;transition: color 0.15s ease-in-out, background-color 0.15s ease-in-out, border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;'>{button_description}</a>";

    private static string CallToAction(string actionName, string actionLink)
    {
        var button = $"{BUTTON_HTML}".Replace("{button_description}", actionName, System.StringComparison.InvariantCulture);
        return button.Replace("{button_link}", actionLink, System.StringComparison.InvariantCulture);
    }

    private static string CreateEmailBody(string body) => $"<html><body style='{BODY_STYLE}'>{HEADER_HTML}{body}</body></html>";
    private void RegisterServerEmailTriggers(IServiceCollection services)
    {

        List<EmailTriggerViewModel> vms = new();

        var vm = new EmailTriggerViewModel()
        {
            Trigger = ServerMailTriggers.PasswordReset,
            RecipientExample = "{User.MailboxAddress}",
            SubjectExample = "Update Password",
            BodyExample = CreateEmailBody($"A request has been made to reset your {{Branding.ServerName}} password. Please set your password by clicking below.<br/><br/>{CallToAction("Update Password", "{ResetLink}")}"),
            PlaceHolders = new()
            {
                new ("{ResetLink}", "The link to the password reset page")
            },
            Description = "Password Reset Requested",
        };
        vms.Add(vm);
        var commonPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{User.Name}", "The name of the user (eg. John Doe)"),
            new("{User.Email}", "The email of the user (eg. john.doe@example.com)"),
            new("{User.MailboxAddress}", "The formatted mailbox address to use when sending an email. (eg. \"John Doe\" <john.doe@example.com>)"),
            new("{Branding.ServerName}", "The name of the server (You can configure this in Server Settings ➡ Branding)"),
            new("{Branding.ContactUrl}", "The contact URL of the server (You can configure this in Server Settings ➡ Branding)"),
        };
        foreach (var v in vms)
        {
            v.ServerTrigger = true;
            v.PlaceHolders.InsertRange(0, commonPlaceholders);
            services.AddSingleton(v);
        }
    }
}
