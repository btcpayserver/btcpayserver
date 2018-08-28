using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.Charge
{
    //[{"type":"ipv4","address":"52.166.90.122","port":9735}]
    public class GetInfoResponse
    {
        public class GetInfoAddress
        {
            public string Type { get; set; }
            public string Address { get; set; }
            public int Port { get; set; }
        }
        public string Id { get; set; }
        public GetInfoAddress[] Address { get; set; }
        public string Version { get; set; }
        public int BlockHeight { get; set; }
        public string Network { get; set; }
    }
}
