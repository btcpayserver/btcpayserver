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
            if (paymentMethodId.PaymentType == PaymentTypes.BTCLike)
            {
                // Legacy
                if (value.Type == JTokenType.String)
                {
                    return DerivationStrategy.Parse(((JValue)value).Value<string>(), network, true);
                }
                //////////
                var stratData = value.ToObject<DerivationStrategyData>();
                return DerivationStrategy.Parse(stratData.DerivationStrategy, network, stratData.Enabled);
                ;            }
            else if (paymentMethodId.PaymentType == PaymentTypes.LightningLike)
            {
                return JsonConvert.DeserializeObject<Payments.Lightning.LightningSupportedPaymentMethod>(value.ToString());
            }
            throw new NotSupportedException();
        }

        public static IPaymentMethodDetails DeserializePaymentMethodDetails(PaymentMethodId paymentMethodId, JObject jobj)
        {
            if(paymentMethodId.PaymentType == PaymentTypes.BTCLike)
            {
                return JsonConvert.DeserializeObject<Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod>(jobj.ToString());
            }
            if (paymentMethodId.PaymentType == PaymentTypes.LightningLike)
            {
                return JsonConvert.DeserializeObject<Payments.Lightning.LightningLikePaymentMethodDetails>(jobj.ToString());
            }
            throw new NotSupportedException(paymentMethodId.PaymentType.ToString());
        }


        public static JToken Serialize(ISupportedPaymentMethod factory)
        {
            var str = JsonConvert.SerializeObject(factory);
            return JObject.Parse(str);
        }

    }
}
