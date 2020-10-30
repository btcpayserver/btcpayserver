using BTCPayServer.Client.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Events
{
    public interface IGreenFieldEvent
    {
        string EventType { get; set; }
        object Payload { get; set; }
        string Signature { get; set; }
        void SetSignature(string url, Key key);
        bool VerifySignature(string url, PubKey key);
        string GetPayload();
    }

    public class GreenFieldEvent<T> : IGreenFieldEvent
    {
        public string EventType { get; set; }
        public object Payload { get; set; }

        [JsonIgnore]
        public T PayloadParsed { get{ return (T) Payload;} set { Payload = value; } }
        public string Signature { get; set; }
        
        public void SetSignature(string url, Key key)
        {
            Signature = key.SignMessage($"{url}_{GetPayload()}");
        }

        public bool VerifySignature(string url, PubKey key)
        {
            return key.VerifyMessage($"{url}_{GetPayload()}", Signature);
        }

        public virtual string GetPayload()
        {
            return JsonConvert.SerializeObject(Payload);
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
