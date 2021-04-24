using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet("{storeId}/integrations")]
        public IActionResult Integrations()
        {            
            return View("Integrations",new IntegrationsViewModel());
        }

        [HttpGet("{storeId}/webhooks")]
        public async Task<IActionResult> Webhooks()
        {
            var webhooks = await _Repo.GetWebhooks(CurrentStore.Id);
            return View(nameof(Webhooks), new WebhooksViewModel()
            {
                Webhooks = webhooks.Select(w => new WebhooksViewModel.WebhookViewModel()
                {
                    Id = w.Id,
                    Url = w.GetBlob().Url
                }).ToArray()
            });
        }

        [HttpGet("{storeId}/webhooks/new")]
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

        [HttpGet("{storeId}/webhooks/{webhookId}/remove")]
        public async Task<IActionResult> DeleteWebhook(string webhookId)
        {
            var webhook = await _Repo.GetWebhook(CurrentStore.Id, webhookId);
            if (webhook is null)
                return NotFound();

            return View("Confirm", new ConfirmModel
            {
                Title = $"Delete a webhook",
                Description = "This webhook will be removed from this store, do you wish to continue?",
                Action = "Delete"
            });
        }

        [HttpPost("{storeId}/webhooks/{webhookId}/remove")]
        public async Task<IActionResult> DeleteWebhookPost(string webhookId)
        {
            var webhook = await _Repo.GetWebhook(CurrentStore.Id, webhookId);
            if (webhook is null)
                return NotFound();

            await _Repo.DeleteWebhook(CurrentStore.Id, webhookId);
            TempData[WellKnownTempData.SuccessMessage] = "Webhook successfully deleted";
            return RedirectToAction(nameof(Webhooks), new { storeId = CurrentStore.Id });
        }

        [HttpPost("{storeId}/webhooks/new")]
        public async Task<IActionResult> NewWebhook(string storeId, EditWebhookViewModel viewModel)
        {
            if (!ModelState.IsValid)
                return View(nameof(ModifyWebhook), viewModel);

            await _Repo.CreateWebhook(CurrentStore.Id, viewModel.CreateBlob());
            TempData[WellKnownTempData.SuccessMessage] = "The webhook has been created";
            return RedirectToAction(nameof(Webhooks), new { storeId });
        }

        [HttpGet("{storeId}/webhooks/{webhookId}")]
        public async Task<IActionResult> ModifyWebhook(string webhookId)
        {
            var webhook = await _Repo.GetWebhook(CurrentStore.Id, webhookId);
            if (webhook is null)
                return NotFound();

            var blob = webhook.GetBlob();
            var deliveries = await _Repo.GetWebhookDeliveries(CurrentStore.Id, webhookId, 20);
            return View(nameof(ModifyWebhook), new EditWebhookViewModel(blob)
            {
                Deliveries = deliveries
                            .Select(s => new DeliveryViewModel(s)).ToList()
            });
        }

        [HttpPost("{storeId}/webhooks/{webhookId}")]
        public async Task<IActionResult> ModifyWebhook(string webhookId, EditWebhookViewModel viewModel)
        {
            var webhook = await _Repo.GetWebhook(CurrentStore.Id, webhookId);
            if (webhook is null)
                return NotFound();

            await _Repo.UpdateWebhook(CurrentStore.Id, webhookId, viewModel.CreateBlob());
            TempData[WellKnownTempData.SuccessMessage] = "The webhook has been updated";
            return RedirectToAction(nameof(Webhooks), new { storeId = CurrentStore.Id });
        }

        [HttpPost("{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver")]
        public async Task<IActionResult> RedeliverWebhook(string webhookId, string deliveryId)
        {
            var delivery = await _Repo.GetWebhookDelivery(CurrentStore.Id, webhookId, deliveryId);
            if (delivery is null)
                return NotFound();

            var newDeliveryId = await WebhookNotificationManager.Redeliver(deliveryId);
            if (newDeliveryId is null)
                return NotFound();

            TempData[WellKnownTempData.SuccessMessage] = "Successfully planned a redelivery";
            return RedirectToAction(nameof(ModifyWebhook),
                new
                {
                    storeId = CurrentStore.Id,
                    webhookId
                });
        }

        [HttpGet("{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request")]
        public async Task<IActionResult> WebhookDelivery(string webhookId, string deliveryId)
        {
            var delivery = await _Repo.GetWebhookDelivery(CurrentStore.Id, webhookId, deliveryId);
            if (delivery is null)
                return NotFound();

            return File(delivery.GetBlob().Request, "application/json");
        }
    }
}
