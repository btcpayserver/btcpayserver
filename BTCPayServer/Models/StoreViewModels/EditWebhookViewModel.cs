using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Validation;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DeliveryViewModel
    {
        public DeliveryViewModel()
        {

        }
        public DeliveryViewModel(Data.WebhookDeliveryData s)
        {
            var blob = s.GetBlob();
            Id = s.Id;
            Success = blob.Status == WebhookDeliveryStatus.HttpSuccess;
            ErrorMessage = blob.ErrorMessage ?? "Success";
            Time = s.Timestamp;
            var evt = blob.ReadRequestAs<WebhookEvent>();
            Type = evt.Type;
            Pruned = evt.IsPruned();
            WebhookId = s.Id;
            PayloadUrl = s.Webhook?.GetBlob().Url;
        }
        public string Id { get; set; }
        public DateTimeOffset Time { get; set; }
        public string Type { get; private set; }
        public bool Pruned { get; set; }
        public string WebhookId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string PayloadUrl { get; set; }
    }
    public class EditWebhookViewModel
    {
        public EditWebhookViewModel()
        {

        }
        public EditWebhookViewModel(WebhookBlob blob)
        {
            Active = blob.Active;
            AutomaticRedelivery = blob.AutomaticRedelivery;
            Everything = blob.AuthorizedEvents.Everything;
            Events = blob.AuthorizedEvents.SpecificEvents;
            PayloadUrl = blob.Url;
            Secret = blob.Secret;
            IsNew = false;
        }
        public bool IsNew { get; set; }
        public bool Active { get; set; }
        public bool AutomaticRedelivery { get; set; }
        public bool Everything { get; set; }
        public string[] Events { get; set; } = Array.Empty<string>();
        [Uri]
        [Required]
        public string PayloadUrl { get; set; }
        [MaxLength(64)]
        public string Secret { get; set; }

        public List<DeliveryViewModel> Deliveries { get; set; } = new List<DeliveryViewModel>();

        public WebhookBlob CreateBlob()
        {
            return new WebhookBlob()
            {
                Active = Active,
                Secret = Secret,
                AutomaticRedelivery = AutomaticRedelivery,
                Url = new Uri(PayloadUrl, UriKind.Absolute).AbsoluteUri,
                AuthorizedEvents = new AuthorizedWebhookEvents()
                {
                    Everything = Everything,
                    SpecificEvents = Events
                }
            };
        }
    }
}
