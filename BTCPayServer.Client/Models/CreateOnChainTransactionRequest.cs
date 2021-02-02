using System.Collections.Generic;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreateOnChainTransactionRequest
    {

        public class CreateOnChainTransactionRequestDestination
        {
            public string Destination { get; set; }
            public decimal? Amount { get; set; }
            public bool SubtractFromAmount { get; set; }
        }
        [JsonConverter(typeof(FeeRateJsonConverter))]
        public FeeRate FeeSatoshiPerByte { get; set; }
        public bool ProceedWithPayjoin { get; set; }
        public bool NoChange { get; set; }
        [JsonProperty( ItemConverterType = typeof(OutpointJsonConverter))]
        public List<OutPoint> SelectedInputs { get; set; }
        public List<CreateOnChainTransactionRequestDestination> Destinations { get; set; }
        public bool? RBF { get; set; }
    }
}
