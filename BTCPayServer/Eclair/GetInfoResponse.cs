using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Eclair
{
    public class GetInfoResponse
    {
        public string NodeId { get; set; }
        public string Alias { get; set; }
        public int Port { get; set; }
        public uint256 ChainHash { get; set; }
        public int BlockHeight { get; set; }
    }
}
