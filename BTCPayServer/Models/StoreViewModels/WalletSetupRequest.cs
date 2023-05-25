using System.Collections.Generic;
using NBXplorer.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class WalletSetupRequest : GenerateWalletRequest
    {
        public bool PayJoinEnabled { get; set; }
        public bool CanUsePayJoin { get; set; }
        
        public new Dictionary<string, string> AdditionalOptions { get; set; }
    }
}
