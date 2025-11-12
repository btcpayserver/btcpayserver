using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Emails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using MimeKit;

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Authorize(Policy = Client.Policies.CanModifyServerSettings,
    AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIServerEmailController(
    EmailSenderFactory emailSenderFactory,
    PoliciesSettings policiesSettings,
    SettingsRepository settingsRepository,
    IStringLocalizer stringLocalizer
    ) : UIEmailControllerBase(stringLocalizer)
{
    protected override async Task<EmailSettings> GetEmailSettings(Context ctx)
        => await emailSenderFactory.GetSettings() ?? new EmailSettings();

    protected override async Task SaveEmailSettings(Context ctx, EmailSettings settings, EmailsViewModel viewModel = null)
    {
        await settingsRepository.UpdateSetting(settings);
        if (viewModel is not null)
        {
            if (policiesSettings.DisableStoresToUseServerEmailSettings == viewModel.EnableStoresToUseServerEmailSettings)
            {
                policiesSettings.DisableStoresToUseServerEmailSettings = !viewModel.EnableStoresToUseServerEmailSettings;
                await settingsRepository.UpdateSetting(policiesSettings);
            }
        }
    }

    protected override IActionResult RedirectToEmailSettings(Context ctx)
    => RedirectToAction(nameof(ServerEmailSettings));

    protected override async Task<(string Subject, string Body)> GetTestMessage(Context ctx)
    {
        var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>();
        var serverName = string.IsNullOrEmpty(serverSettings?.ServerName) ? "BTCPay Server" : serverSettings.ServerName;
        return ($"{serverName}: Email test",
            "You received it, the BTCPay Server SMTP settings work.");
    }

    private Context CreateContext()
        => new()
        {
            CreateEmailViewModel = (email) => new EmailsViewModel(email)
            {
                EnableStoresToUseServerEmailSettings = !policiesSettings.DisableStoresToUseServerEmailSettings,
                ModifyPermission = Policies.CanModifyServerSettings,
                ViewPermission = Policies.CanModifyServerSettings
            }
        };

    [HttpGet("server/emails")]
    public Task<IActionResult> ServerEmailSettings()
        => EmailSettingsCore(CreateContext());

    [HttpPost("server/emails")]
    public Task<IActionResult> ServerEmailSettings(EmailsViewModel model, string command)
        => EmailSettingsCore(CreateContext(), model, command);
}
