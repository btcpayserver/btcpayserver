using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class PaymentMethodExtensions
    {
        public static ISupportedPaymentMethod Deserialize(PaymentMethodId paymentMethodId, JToken value, BTCPayNetwork network)
        {
            // Legacy
            if (paymentMethodId.PaymentType == PaymentTypes.BTCLike)
            {
                return BTCPayServer.DerivationStrategy.Parse(((JValue)value).Value<string>(), network);
            }
            //////////
            else // if(paymentMethodId.PaymentType == PaymentTypes.Lightning)
            {
                // return JsonConvert.Deserialize<T>();
            }
            throw new NotSupportedException();
        }

        public static JToken Serialize(ISupportedPaymentMethod factory)
        {
            // Legacy
            if (factory.PaymentId.PaymentType == PaymentTypes.BTCLike)
            {
                return new JValue(((DerivationStrategy)factory).DerivationStrategyBase.ToString());
            }
            //////////////
            else
            {
                var str = JsonConvert.SerializeObject(factory);
                return JObject.Parse(str);
            }
            throw new NotSupportedException();
        }
    }
}
