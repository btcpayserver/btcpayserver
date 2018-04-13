using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class PoliciesSettings
    {
        public bool RequiresConfirmedEmail
        {
            get; set;
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool LockSubscription { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string CustomBootstrapThemeCssUri { get; set; }
    }
}
