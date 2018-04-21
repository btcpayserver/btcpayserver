using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class ThemeSettings
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string BootstrapCssUri { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string CreativeStartCssUri { get; set; }
    }
}
