using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer;
namespace BTCPayServer
{
    public class MoneroLikeSpecificBtcPayNetwork : BTCPayNetworkBase
    {
        public int MaxTrackedConfirmation = 10;
    }
}
