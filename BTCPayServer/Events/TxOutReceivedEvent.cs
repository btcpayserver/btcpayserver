using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Events
{
    public class TxOutReceivedEvent
    {
        public Script ScriptPubKey { get; set; }
        public BitcoinAddress Address { get; set; }

        public override string ToString()
        {
            String address = Address?.ToString() ?? ScriptPubKey.ToHex();
            return $"{address} received a transaction";
        }
    }
}
