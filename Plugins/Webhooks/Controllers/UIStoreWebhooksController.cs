#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Webhooks.Views;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.Webhooks.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
[Area(WebhooksPlugin.Area)]
public class UIStoreWebhooksController(
    StoreRepository storeRepo,
    IStringLocalizer stringLocalizer,
    WebhookSender webhookSender) : Controller
{
    public Data.StoreData CurrentStore => HttpContext.GetStoreData();
    public IStringLocalizer StringLocalizer { get; set; } = stringLocalizer;
    private async Task<Data.WebhookDeliveryData?> LastDeliveryForWebhook(string webhookId)
    {
        return (await storeRepo.GetWebhookDeliveries(CurrentStore.Id, webhookId, 1)).FirstOrDefault();
    }

    [HttpGet("{storeId}/webhooks")]
    public async Task<IActionResult> Webhooks()
    {
        var webhooks = await storeRepo.GetWebhooks(CurrentStore.Id);
        return View(nameof(Webhooks), new WebhooksViewModel
        {
            Webhooks = webhooks.Select(async w =>
                {
                    var lastDelivery = await LastDeliveryForWebhook(w.Id);
                    var lastDeliveryBlob = lastDelivery?.GetBlob();

                    return new WebhooksViewModel.WebhookViewModel()
                    {
                        Id = w.Id,
                        Url = w.GetBlob().Url,
                        LastDeliveryErrorMessage = lastDeliveryBlob?.ErrorMessage,
                        LastDeliveryTimeStamp = lastDelivery?.Timestamp,
                        LastDeliverySuccessful = lastDeliveryBlob == null || lastDeliveryBlob.Status == WebhookDeliveryStatus.HttpSuccess,
                    };
                }
            ).Select(t => t.Result).ToArray()
        });
    }

    [HttpGet("{storeId}/webhooks/new")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult NewWebhook()
    {
        return View(nameof(ModifyWebhook), new EditWebhookViewModel
        {
            Active = true,
            Everything = true,
            IsNew = true,
            Secret = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20))
        });
    }

    [HttpPost("{storeId}/webhooks/{webhookId}/remove")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteWebhook(string webhookId)
    {
        var webhook = await storeRepo.GetWebhook(CurrentStore.Id, webhookId);
        if (webhook is null)
            return NotFound();

        await storeRepo.DeleteWebhook(CurrentStore.Id, webhookId);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Webhook successfully deleted"].Value;
        return RedirectToAction(nameof(Webhooks), new { storeId = CurrentStore.Id });
    }

    [HttpPost("{storeId}/webhooks/new")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> NewWebhook(string storeId, EditWebhookViewModel viewModel)
    {
        if (!ModelState.IsValid)
            return View(nameof(ModifyWebhook), viewModel);

        await storeRepo.CreateWebhook(CurrentStore.Id, viewModel.CreateBlob());
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The webhook has been created"].Value;
        return RedirectToAction(nameof(Webhooks), new { storeId });
    }

    [HttpGet("{storeId}/webhooks/{webhookId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ModifyWebhook(string webhookId)
    {
        var webhook = await storeRepo.GetWebhook(CurrentStore.Id, webhookId);
        if (webhook is null)
            return NotFound();

        var deliveries = await storeRepo.GetWebhookDeliveries(CurrentStore.Id, webhookId, 20);
        return View(nameof(ModifyWebhook), new EditWebhookViewModel(webhook.GetBlob())
        {
            Deliveries = deliveries
                .Select(s => new DeliveryViewModel(s)).ToList()
        });
    }

    [HttpPost("{storeId}/webhooks/{webhookId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ModifyWebhook(string webhookId, EditWebhookViewModel viewModel)
    {
        var webhook = await storeRepo.GetWebhook(CurrentStore.Id, webhookId);
        if (webhook is null)
            return NotFound();
        if (!ModelState.IsValid)
            return View(nameof(ModifyWebhook), viewModel);

        await storeRepo.UpdateWebhook(CurrentStore.Id, webhookId, viewModel.CreateBlob());
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The webhook has been updated"].Value;
        return RedirectToAction(nameof(Webhooks), new { storeId = CurrentStore.Id });
    }

    [HttpPost("{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> RedeliverWebhook(string webhookId, string deliveryId)
    {
        var delivery = await storeRepo.GetWebhookDelivery(CurrentStore.Id, webhookId, deliveryId);
        if (delivery is null)
            return NotFound();

        var newDeliveryId = await webhookSender.Redeliver(deliveryId);
        if (newDeliveryId is null)
            return NotFound();

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Successfully planned a redelivery"].Value;
        return RedirectToAction(nameof(ModifyWebhook),
            new
            {
                storeId = CurrentStore.Id,
                webhookId
            });
    }

    [HttpGet("{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> WebhookDelivery(string webhookId, string deliveryId)
    {
        var delivery = await storeRepo.GetWebhookDelivery(CurrentStore.Id, webhookId, deliveryId);
        if (delivery?.GetBlob()?.Request is { } request)
            return File(request, "application/json");
        return NotFound();
    }
}
