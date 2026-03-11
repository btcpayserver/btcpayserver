using System;
using System.Linq;
using System.Text;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Webhooks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class AuthorizedWebhookEvents
    {
        public bool Everything { get; set; }

        public string[] SpecificEvents { get; set; } = Array.Empty<string>();
        public bool Match(string evt)
        {
            return Everything || SpecificEvents.Contains(evt);
        }
    }


    public class WebhookDeliveryBlob
    {
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public WebhookDeliveryStatus Status { get; set; }
        public int? HttpCode { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public byte[] Request { get; set; }
        public void Prune()
        {
            var request = JObject.Parse(UTF8Encoding.UTF8.GetString(Request));
            foreach (var prop in request.Properties().ToList())
            {
                if (prop.Name == "type")
                    continue;
                prop.Remove();
            }
            Request = UTF8Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
        }
        public T ReadRequestAs<T>()
        {
            if (Request is null)
                throw new InvalidOperationException("No request");
            return JsonConvert.DeserializeObject<T>(UTF8Encoding.UTF8.GetString(Request), WebhookSender.DefaultSerializerSettings);
        }

        public bool IsPruned()
        {
            return Request is null || ReadRequestAs<WebhookEvent>().IsPruned();
        }
    }
    public class WebhookBlob
    {
        public string Url { get; set; }
        public bool Active { get; set; } = true;
        public string Secret { get; set; }
        public bool AutomaticRedelivery { get; set; }
        public AuthorizedWebhookEvents AuthorizedEvents { get; set; }
        public bool ShouldDeliver(string type)
        => Active && AuthorizedEvents.Match(type);
    }
#nullable enable
    public static class WebhookDataExtensions
    {
        public static WebhookBlob GetBlob(this WebhookData webhook)
        {
            return webhook.HasTypedBlob<WebhookBlob>().GetBlob() ?? throw new InvalidOperationException("No WebhookBlob, this should not happen");
        }
        public static void SetBlob(this WebhookData webhook, WebhookBlob blob)
        {
            ArgumentNullException.ThrowIfNull(blob);
            webhook.HasTypedBlob<WebhookBlob>().SetBlob(blob);
        }
        public static WebhookDeliveryBlob? GetBlob(this WebhookDeliveryData webhook)
        {
            return webhook.Blob is null ? null : JsonConvert.DeserializeObject<WebhookDeliveryBlob>(webhook.Blob, WebhookSender.DefaultSerializerSettings);
        }
        public static void SetBlob(this WebhookDeliveryData webhook, WebhookDeliveryBlob? blob)
        {
            webhook.Blob = blob is null ? null : JsonConvert.SerializeObject(blob, WebhookSender.DefaultSerializerSettings);
        }
    }
#nullable restore
}
