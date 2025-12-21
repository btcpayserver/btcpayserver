using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payouts;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public interface IJsonConverterRegistration
    {
        JsonConverter CreateJsonConverter(BTCPayNetwork network);
    }
    public class JsonConverterRegistration : IJsonConverterRegistration
    {
        internal readonly Func<BTCPayNetwork, JsonConverter> _createConverter;
        public JsonConverterRegistration(Func<BTCPayNetwork, JsonConverter> createConverter)
        {
            _createConverter = createConverter;
        }
        public JsonConverter CreateJsonConverter(BTCPayNetwork network)
        {
            return _createConverter(network);
        }
    }
    public class BTCPayNetworkJsonSerializerSettings
    {
        public BTCPayNetworkJsonSerializerSettings(BTCPayNetworkProvider networkProvider, IEnumerable<IJsonConverterRegistration> jsonSerializers)
        {
            foreach (var network in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var serializer = new JsonSerializerSettings();
                foreach (var jsonSerializer in jsonSerializers)
                {
                    serializer.Converters.Add(jsonSerializer.CreateJsonConverter(network));
                }
                foreach (var converter in network.NBXplorerNetwork.JsonSerializerSettings.Converters)
                {
                    serializer.Converters.Add(converter);
                }
                // TODO: Get rid of this serializer
                _Serializers.Add(PayoutTypes.CHAIN.GetPayoutMethodId(network.CryptoCode), serializer);
                _Serializers.Add(PayoutTypes.LN.GetPayoutMethodId(network.CryptoCode), serializer);
            }
        }

        readonly Dictionary<PayoutMethodId, JsonSerializerSettings> _Serializers = new Dictionary<PayoutMethodId, JsonSerializerSettings>();
        public JsonSerializerSettings GetSerializer(PayoutMethodId payoutMethodId)
        {
            ArgumentNullException.ThrowIfNull(payoutMethodId);
            _Serializers.TryGetValue(payoutMethodId, out var serializer);
            return serializer;
        }
    }

}
