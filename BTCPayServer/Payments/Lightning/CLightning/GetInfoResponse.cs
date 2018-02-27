using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class GetInfoResponse
    {
        public string Id { get; set; }
        public int Port { get; set; }
        public string[] Address { get; set; }
        public string Version { get; set; }
        public int BlockHeight { get; set; }
        public string Network { get; set; }
    }
}
