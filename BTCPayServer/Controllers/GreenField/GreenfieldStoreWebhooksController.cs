using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield,
               Policy = Policies.CanModifyWebhooks)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreWebhooksController : ControllerBase
    {
        public GreenfieldStoreWebhooksController(StoreRepository storeRepository, WebhookSender webhookSender)
        {
            StoreRepository = storeRepository;
            WebhookSender = webhookSender;
        }

        public StoreRepository StoreRepository { get; }
        public WebhookSender WebhookSender { get; }

        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId?}")]
        public async Task<IActionResult> ListWebhooks(string storeId, string webhookId)
        {
            if (webhookId is null)
            {
                return Ok((await StoreRepository.GetWebhooks(CurrentStoreId))
                        .Select(o => FromModel(o, false))
                        .ToArray());
            }
            else
            {
                var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
                if (w is null)
                    return WebhookNotFound();
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
        public async Task<IActionResult> CreateWebhook(string storeId, Client.Models.CreateStoreWebhookRequest create)
        {
            ValidateWebhookRequest(create);
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var webhookId = await StoreRepository.CreateWebhook(CurrentStoreId, ToModel(create));
            var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
            if (w is null)
                return WebhookNotFound();
            return Ok(FromModel(w, true));
        }

        private void ValidateWebhookRequest(StoreWebhookBaseData create)
        {
            if (!Uri.TryCreate(create?.Url, UriKind.Absolute, out _))
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
                return WebhookNotFound();
            await StoreRepository.UpdateWebhook(storeId, webhookId, ToModel(update));
            return await ListWebhooks(storeId, webhookId);
        }
        [HttpDelete("~/api/v1/stores/{storeId}/webhooks/{webhookId}")]
        public async Task<IActionResult> DeleteWebhook(string storeId, string webhookId)
        {
            var w = await StoreRepository.GetWebhook(CurrentStoreId, webhookId);
            if (w is null)
                return WebhookNotFound();
            await StoreRepository.DeleteWebhook(CurrentStoreId, webhookId);
            return Ok();
        }

        IActionResult WebhookNotFound()
        {
            return this.CreateAPIError(404, "webhook-not-found", "The webhook was not found");
        }
        IActionResult WebhookDeliveryNotFound()
        {
            return this.CreateAPIError(404, "webhookdelivery-not-found", "The webhook delivery was not found");
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
                                    } :
                                    new AuthorizedWebhookEvents() { Everything = true },
                AutomaticRedelivery = create.AutomaticRedelivery,
            };
        }


        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId?}")]
        public async Task<IActionResult> ListDeliveries(string storeId, string webhookId, string deliveryId, int? count = null)
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
                    return WebhookDeliveryNotFound();
                return Ok(FromModel(delivery));
            }
        }
        [HttpPost("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver")]
        public async Task<IActionResult> RedeliverWebhook(string storeId, string webhookId, string deliveryId)
        {
            var delivery = await StoreRepository.GetWebhookDelivery(CurrentStoreId, webhookId, deliveryId);
            if (delivery is null)
                return WebhookDeliveryNotFound();
            if (delivery.GetBlob().IsPruned())
                return WebhookDeliveryPruned();
            return this.Ok(new JValue(await WebhookSender.Redeliver(deliveryId)));
        }

        private IActionResult WebhookDeliveryPruned()
        {
            return this.CreateAPIError(409, "webhookdelivery-pruned", "This webhook delivery has been pruned, so it can't be redelivered");
        }

        [HttpGet("~/api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request")]
        public async Task<IActionResult> GetDeliveryRequest(string storeId, string webhookId, string deliveryId)
        {
            var delivery = await StoreRepository.GetWebhookDelivery(CurrentStoreId, webhookId, deliveryId);
            if (delivery is null)
                return WebhookDeliveryNotFound();
            if (delivery.GetBlob().IsPruned())
                return WebhookDeliveryPruned();
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
