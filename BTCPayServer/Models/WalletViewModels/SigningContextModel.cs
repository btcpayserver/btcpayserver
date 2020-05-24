using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class SigningContextModel
    {
        public string PayJoinEndpointUrl { get; set; }
        public bool? EnforceLowR { get; set; }
    }
}
