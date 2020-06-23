﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
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
            foreach (var network in networkProvider.UnfilteredNetworks.GetAll().OfType<BTCPayNetwork>())
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
                _Serializers.Add(network.CryptoCode, serializer);
            }
        }

        Dictionary<string, JsonSerializerSettings> _Serializers = new Dictionary<string, JsonSerializerSettings>();

        public JsonSerializerSettings GetSerializer(Network network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return GetSerializer(network.NetworkSet.CryptoCode);
        }
        public JsonSerializerSettings GetSerializer(BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return GetSerializer(network.CryptoCode);
        }
        public JsonSerializerSettings GetSerializer(string cryptoCode)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            _Serializers.TryGetValue(cryptoCode, out var serializer);
            return serializer;
        }
    }

}
