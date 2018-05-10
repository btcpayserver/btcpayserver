using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class PoliciesSettings
    {
        [Display(Name = "Requires a confirmation mail for registering")]
        public bool RequiresConfirmedEmail
        {
            get; set;
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Disable registration")]
        public bool LockSubscription { get; set; }
    }
}
