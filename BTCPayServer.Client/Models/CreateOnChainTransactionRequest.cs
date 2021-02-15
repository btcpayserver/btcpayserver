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
        public FeeRate FeeRate { get; set; }
        public bool ProceedWithPayjoin { get; set; }= true;
        public bool ProceedWithBroadcast { get; set; } = true;
        public bool NoChange { get; set; } = false;

        [JsonProperty(ItemConverterType = typeof(OutpointJsonConverter))]
        public List<OutPoint> SelectedInputs { get; set; } = null;
        public List<CreateOnChainTransactionRequestDestination> Destinations { get; set; }
        public bool? RBF { get; set; } = null;
    }
}
