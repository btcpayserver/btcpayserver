using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield,
               Policy = Policies.CanModifyStoreWebhooks)]
    [EnableCors(CorsPolicies.All)]
    public class StoreWebhooksController : ControllerBase
    {
        public StoreWebhooksController(StoreRepository storeRepository, WebhookNotificationManager webhookNotificationManager)
        {
            StoreRepository = storeRepository;
            WebhookNotificationManager = webhookNotificationManager;
        }

        public StoreRepository StoreRepository { get; }
        public WebhookNotificationManager WebhookNotificationManager { get; }

        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId?}")]
        public async Task<IActionResult> ListWebhooks(string webhookId)
        {
            if (webhookId is null)
            {
                return Ok((await StoreRepository.GetWebhooks(CurrentStoreId))
                        .Select(o => FromModel(o, false))
                        .ToList());
            }
            else
            {
                var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
                if (w is null)
                    return NotFound();
                return Ok(FromModel(w, false));
            }
        }

        string CurrentStoreId
        {
            get
            {
                return this.HttpContext.GetStoreData()?.Id;
            }
        }

        [HttpPost("~/api/v1/stores/{storeId}/webhooks")]
        public async Task<IActionResult> CreateWebhook(Client.Models.CreateStoreWebhookRequest create)
        {
            ValidateWebhookRequest(create);
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var webhookId = await StoreRepository.CreateWebhook(CurrentStoreId, ToModel(create));
            var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
            if (w is null)
                return NotFound();
            return Ok(FromModel(w, true));
        }

        private void ValidateWebhookRequest(StoreWebhookBaseData create)
        {
            if (!Uri.TryCreate(create?.Url, UriKind.Absolute, out var uri))
                ModelState.AddModelError(nameof(Url), "Invalid Url");
        }

        [HttpPut("~/api/v1/stores/{storeId}/webhooks/{webhookId}")]
        public async Task<IActionResult> UpdateWebhook(string storeId, string webhookId, Client.Models.UpdateStoreWebhookRequest update)
        {
            ValidateWebhookRequest(update);
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
            if (w is null)
                return NotFound();
            await StoreRepository.UpdateWebhook(storeId, webhookId, ToModel(update));
            return await ListWebhooks(webhookId);
        }
        [HttpDelete("~/api/v1/stores/{storeId}/webhooks/{webhookId}")]
        public async Task<IActionResult> DeleteWebhook(string webhookId)
        {
            var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
            if (w is null)
                return NotFound();
            await StoreRepository.DeleteWebhook(CurrentStoreId, webhookId);
            return Ok();
        }
        private WebhookBlob ToModel(StoreWebhookBaseData create)
        {
            return new WebhookBlob()
            {
                Active = create.Enabled,
                Url = create.Url,
                Secret = create.Secret,
                AuthorizedEvents = create.AuthorizedEvents is Client.Models.StoreWebhookBaseData.AuthorizedEventsData aed ?
                                    new AuthorizedWebhookEvents()
                                    {
                                        Everything = aed.Everything,
                                        SpecificEvents = aed.SpecificEvents
                                    }:
                                    new AuthorizedWebhookEvents() { Everything = true },
                AutomaticRedelivery = create.AutomaticRedelivery,                
            };
        }

        
        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId?}")]
        public async Task<IActionResult> ListDeliveries(string webhookId, string deliveryId, int? count = null)
        {
            if (deliveryId is null)
            {
                return Ok((await StoreRepository.GetWebhookDeliveries(CurrentStoreId, webhookId, count))
                        .Select(o => FromModel(o))
                        .ToList());
            }
            else
            {
                var delivery = await StoreRepository.GetWebhookDelivery(CurrentStoreId, webhookId, deliveryId);
                if (delivery is null)
                    return NotFound();
                return Ok(FromModel(delivery));
            }
        }
        [HttpPost("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver")]
        public async Task<IActionResult> RedeliverWebhook(string webhookId, string deliveryId)
        {
            var delivery = await StoreRepository.GetWebhookDelivery(CurrentStoreId, webhookId, deliveryId);
            if (delivery is null)
                return NotFound();
            return this.Ok(new JValue(await WebhookNotificationManager.Redeliver(deliveryId)));
        }

        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request")]
        public async Task<IActionResult> GetDeliveryRequest(string webhookId, string deliveryId)
        {
            var delivery = await StoreRepository.GetWebhookDelivery(CurrentStoreId, webhookId, deliveryId);
            if (delivery is null)
                return NotFound();
            return File(delivery.GetBlob().Request, "application/json");
        }

        private Client.Models.WebhookDeliveryData FromModel(Data.WebhookDeliveryData data)
        {
            var b = data.GetBlob();
            return new Client.Models.WebhookDeliveryData()
            {
                Id = data.Id,
                Timestamp = data.Timestamp,
                Status = b.Status,
                ErrorMessage = b.ErrorMessage,
                HttpCode = b.HttpCode
            };
        }

        Client.Models.StoreWebhookData FromModel(Data.WebhookData data, bool includeSecret)
        {
            var b = data.GetBlob();
            return new Client.Models.StoreWebhookData()
            {
                Id = data.Id,
                Url = b.Url,
                Enabled = b.Active,
                Secret = includeSecret ? b.Secret : null,
                AutomaticRedelivery = b.AutomaticRedelivery,
                AuthorizedEvents = new Client.Models.StoreWebhookData.AuthorizedEventsData()
                {
                    Everything = b.AuthorizedEvents.Everything,
                    SpecificEvents = b.AuthorizedEvents.SpecificEvents
                }
            };
        }
    }
}
