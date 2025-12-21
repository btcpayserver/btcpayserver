using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentBlob
    {
        public int Version { get; set; } = 2;
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal PaymentMethodFee { get; set; }
        public string Destination { get; set; }
        public JToken Details { get; set; }
        public int Divisibility { get; set; }

        public PaymentBlob SetDetails(IPaymentMethodHandler handler, object details)
        {
            Details = JToken.FromObject(details, handler.Serializer);
            return this;
        }
        public T GetDetails<T>(IPaymentMethodHandler handler) where T : class
        {
            if (handler.Id != handler.Id)
                return null;
            return handler.ParsePaymentDetails(Details) as T;
        }
    }
}
