using BTCPayServer.Client.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Events
{
    public class GreenFieldEvent<T>
    {
        public string EventType { get; set; }
        public T Payload { get; set; }
        public string Signature { get; set; }
        
        public void SetSignature(string url, Key key)
        {
            Signature = key.SignMessage($"{url}_{Payload.GetHashCode()}");
        }

        public bool VerifySignature(string url, PubKey key)
        {
            return key.VerifyMessage($"{url}_{Payload.GetHashCode()}", Signature);
        }
    }
    
    public class InvoiceStatusChangeEventPayload
    {
        public const string EventType = "invoice_status";
        public string InvoiceId { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceExceptionStatus AdditionalStatus { get; set; }
    }
    
}
