using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class PoliciesSettings
    {
        [Display(Name = "Email confirmation required")]
        public bool RequiresConfirmedEmail { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Disable public user registration")]
        public bool LockSubscription { get; set; }
        
        [JsonIgnore]
        [Display(Name = "Enable public user registration")]
        public bool EnableRegistration
        {
            get => !LockSubscription;
            set { LockSubscription = !value; }
        }

        [DefaultValue("English")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Backend's language")]
        public string LangDictionary { get; set; } = "English";

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Admin must approve new users")]
        public bool RequiresUserApproval { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Discourage search engines from indexing this site")]
        public bool DiscourageSearchEngines { get; set; }
        
        [JsonIgnore]
        [Display(Name = "Search engines can index this site")]
        public bool AllowSearchEngines
        {
            get => !DiscourageSearchEngines;
            set { DiscourageSearchEngines = !value; }
        }

        [Display(Name = "Non-admins can use the Internal Lightning Node for their Store")]
        public bool AllowLightningInternalNodeForAll { get; set; }

        [Display(Name = "Non-admins can create Hot Wallets for their Store")]
        public bool AllowHotWalletForAll { get; set; }

        [Display(Name = "Non-admins can import Hot Wallets for their Store")]
        public bool AllowHotWalletRPCImportForAll { get; set; }

        [Display(Name = "Check releases on GitHub and notify when new BTCPay Server version is available")]
        public bool CheckForNewVersions { get; set; }

        [Display(Name = "Disable stores from using the server's email settings as backup")]
        public bool DisableStoresToUseServerEmailSettings { get; set; }
        
        [Display(Name = "Non-admins cannot access the User Creation API Endpoint")]
        public bool DisableNonAdminCreateUserApi { get; set; }
        
        [JsonIgnore]
        [Display(Name = "Non-admins can access the User Creation API Endpoint")]
        public bool EnableNonAdminCreateUserApi
        {
            get => !DisableNonAdminCreateUserApi;
            set { DisableNonAdminCreateUserApi = !value; }
        }

        public const string DefaultPluginSource = "https://plugin-builder.btcpayserver.org";
        [UriAttribute]
        [Display(Name = "Plugin server")]
        public string PluginSource { get; set; }

        [Display(Name = "Show plugins in pre-release")]
        public bool PluginPreReleases { get; set; }
        [Display(Name = "Select the Default Currency during Store Creation")]
        public string DefaultCurrency { get; set; }

        public bool DisableSSHService { get; set; }

        [Display(Name = "Display app on website root")]
        public string RootAppId { get; set; }
        public string RootAppType { get; set; }

        [Display(Name = "Override the block explorers used")]
        public List<BlockExplorerOverrideItem> BlockExplorerLinks { get; set; } = new List<BlockExplorerOverrideItem>();

        public List<DomainToAppMappingItem> DomainToAppMapping { get; set; } = new List<DomainToAppMappingItem>();
        [Display(Name = "Enable experimental features")]
        public bool Experimental { get; set; }
        
        [Display(Name = "Default role for users on a new store")]
        public string DefaultRole { get; set; }

        public class BlockExplorerOverrideItem
        {
            public string CryptoCode { get; set; }
            public string Link { get; set; }
        }

        public class DomainToAppMappingItem
        {
            [Display(Name = "Domain")][Required][HostName] public string Domain { get; set; }
            [Display(Name = "App")][Required] public string AppId { get; set; }

            public string AppType { get; set; }
        }
    }
}
