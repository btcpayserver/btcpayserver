using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SshNet.Security.Cryptography;

namespace BTCPayServer.Data
{
    public class AuthorizedWebhookEvents
    {
        public bool Everything { get; set; }

        [JsonProperty(ItemConverterType = typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public WebhookEventType[] SpecificEvents { get; set; } = Array.Empty<WebhookEventType>();
        public bool Match(WebhookEventType evt)
        {
            return Everything || SpecificEvents.Contains(evt);
        }
    }

    
    public class WebhookDeliveryBlob
    {
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public WebhookDeliveryStatus Status { get; set; }
        public int? HttpCode { get; set; }
        public string ErrorMessage { get; set; }
        public byte[] Request { get; set; }
        public T ReadRequestAs<T>()
        {
            return JsonConvert.DeserializeObject<T>(UTF8Encoding.UTF8.GetString(Request), HostedServices.WebhookNotificationManager.DefaultSerializerSettings);
        }
    }
    public class WebhookBlob
    {
        public string Url { get; set; }
        public bool Active { get; set; } = true;
        public string Secret { get; set; }
        public bool AutomaticRedelivery { get; set; }
        public AuthorizedWebhookEvents AuthorizedEvents { get; set; }
    }
    public static class WebhookDataExtensions
    {
        public static WebhookBlob GetBlob(this WebhookData webhook)
        {
            return JsonConvert.DeserializeObject<WebhookBlob>(Encoding.UTF8.GetString(webhook.Blob));
        }
        public static void SetBlob(this WebhookData webhook, WebhookBlob blob)
        {
            webhook.Blob = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob));
        }
        public static WebhookDeliveryBlob GetBlob(this WebhookDeliveryData webhook)
        {
            return JsonConvert.DeserializeObject<WebhookDeliveryBlob>(ZipUtils.Unzip(webhook.Blob), HostedServices.WebhookNotificationManager.DefaultSerializerSettings);
        }
        public static void SetBlob(this WebhookDeliveryData webhook, WebhookDeliveryBlob blob)
        {
            webhook.Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blob, Formatting.None, HostedServices.WebhookNotificationManager.DefaultSerializerSettings));
        }
    }
}
