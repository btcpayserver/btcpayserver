using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.Eclair
{
    public class AllChannelResponse
    {
        public string ShortChannelId { get; set; }
        public string NodeId1 { get; set; }
        public string NodeId2 { get; set; }
    }
}
