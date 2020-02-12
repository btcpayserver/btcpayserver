using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class PoliciesSettings
    {
        [Display(Name = "Requires a confirmation mail for registering")]
        public bool RequiresConfirmedEmail { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Disable registration")]
        public bool LockSubscription { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Discourage search engines from indexing this site")]
        public bool DiscourageSearchEngines { get; set; }
        [Display(Name = "Allow non-admins to use the internal lightning node in their stores")]
        public bool AllowLightningInternalNodeForAll { get; set; }
        [Display(Name = "Allow non-admins to create hot wallets for their stores")]
        public bool AllowHotWalletForAll { get; set; }
        
        [Display(Name = "Display app on website root")]
        public string RootAppId { get; set; }
        public AppType? RootAppType { get; set; }

        public List<DomainToAppMappingItem> DomainToAppMapping { get; set; } = new List<DomainToAppMappingItem>();

        public class DomainToAppMappingItem
        {
            [Display(Name = "Domain")][Required][HostName] public string Domain { get; set; }
            [Display(Name = "App")][Required]  public string AppId { get; set; }
            
            public AppType AppType { get; set; }
        }
    }
}
