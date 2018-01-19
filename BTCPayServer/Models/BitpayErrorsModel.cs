using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Models
{
    public class BitpayErrorsModel
    {
        public BitpayErrorsModel()
        {

        }
        public BitpayErrorsModel(BitpayHttpException ex)
        {
            Error = ex.Message;
        }

        [JsonProperty("errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public BitpayErrorModel[] Errors
        {
            get; set;
        }
        [JsonProperty("error", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Error
        {
            get; set;
        }
    }

    public class BitpayErrorModel
    {
        [JsonProperty("error")]
        public string Error
        {
            get; set;
        }
    }
}
