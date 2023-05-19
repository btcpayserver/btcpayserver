using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class PoliciesSettings
    {
        [Display(Name = "Require a confirmation email for registering")]
        public bool RequiresConfirmedEmail { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Disable new user registration on the server")]
        public bool LockSubscription { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Discourage search engines from indexing this site")]
        public bool DiscourageSearchEngines { get; set; }
        [Display(Name = "Allow non-admins to use the internal lightning node in their stores")]
        public bool AllowLightningInternalNodeForAll { get; set; }
        [Display(Name = "Allow non-admins to create hot wallets for their stores")]
        public bool AllowHotWalletForAll { get; set; }
        [Display(Name = "Allow non-admins to import hot wallets for their stores")]
        public bool AllowHotWalletRPCImportForAll { get; set; }
        [Display(Name = "Check releases on GitHub and notify when new BTCPay Server version is available")]
        public bool CheckForNewVersions { get; set; }
        [Display(Name = "Disable notifications from automatically showing (no websockets)")]
        public bool DisableInstantNotifications { get; set; }
        [Display(Name = "Disable stores from using the server's email settings as backup")]
        public bool DisableStoresToUseServerEmailSettings { get; set; }
        [Display(Name = "Disable non-admins access to the user creation API endpoint")]
        public bool DisableNonAdminCreateUserApi { get; set; }

        public const string DefaultPluginSource = "https://plugin-builder.btcpayserver.org";
        [UriAttribute]
        [Display(Name = "Plugin server")]
        public string PluginSource { get; set; }
        [Display(Name = "Show plugins in pre-release")]
        public bool PluginPreReleases { get; set; }

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
