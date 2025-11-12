using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Route("stores/{storeId}")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoresEmailController(
    EmailSenderFactory emailSenderFactory,
    StoreRepository storeRepository,
    IStringLocalizer stringLocalizer) : UIEmailControllerBase(stringLocalizer)
{
    private async Task<Context> CreateContext(string storeId)
    {
        var settings = await GetCustomSettings(storeId);
        return new()
        {
            StoreId = storeId,
            CreateEmailViewModel = (email) => new EmailsViewModel(email)
            {
                IsFallbackSetup = settings.Fallback is not null,
                IsCustomSMTP = settings.Custom is not null || settings.Fallback is null,
                StoreId = storeId,
                ModifyPermission = Policies.CanModifyStoreSettings,
                ViewPermission = Policies.CanViewStoreSettings,
            }
        };
    }

    [HttpGet("email-settings")]
    public async Task<IActionResult> StoreEmailSettings(string storeId)
        => await EmailSettingsCore(await CreateContext(storeId));

    [HttpPost("email-settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailSettings(string storeId, EmailsViewModel model, string command)
        => await EmailSettingsCore(await CreateContext(storeId), model, command);

    record AllEmailSettings(EmailSettings Custom, EmailSettings Fallback);
    private async Task<AllEmailSettings> GetCustomSettings(string storeId)
    {
        var sender = await emailSenderFactory.GetEmailSender(storeId) as StoreEmailSender;
        if (sender is null)
            return new(null, null);
        var fallback = sender.FallbackSender is { } fb ? await fb.GetEmailSettings() : null;
        if (fallback?.IsComplete() is not true)
            fallback = null;
        return new(await sender.GetCustomSettings(), fallback);
    }

    protected override async Task<EmailSettings> GetEmailSettings(Context ctx)
    {
        var store = await storeRepository.FindStore(ctx.StoreId);
        return store?.GetStoreBlob().EmailSettings ?? new();
    }

    protected override async Task<EmailSettings> GetEmailSettingsForTest(Context ctx, EmailsViewModel model)
    {
        var settings = await GetCustomSettings(ctx.StoreId);
        return (model.IsCustomSMTP ? model.Settings : settings.Fallback) ?? new();
    }

    protected override async Task SaveEmailSettings(Context ctx, EmailSettings settings, EmailsViewModel viewModel = null)
    {
        var store = await storeRepository.FindStore(ctx.StoreId);
        var blob = store?.GetStoreBlob();
        if (blob is null)
            return;
        blob.EmailSettings = viewModel?.IsCustomSMTP is false ? null : settings;
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
    }

    protected override IActionResult RedirectToEmailSettings(Context ctx)
    => RedirectToAction(nameof(StoreEmailSettings), new { storeId = ctx.StoreId });

    protected override async Task<(string Subject, string Body)> GetTestMessage(Context ctx)
    {
        var store = await storeRepository.FindStore(ctx.StoreId);
        return ($"{store?.StoreName}: Email test", "You received it, the BTCPay Server SMTP settings work.");
    }
}
