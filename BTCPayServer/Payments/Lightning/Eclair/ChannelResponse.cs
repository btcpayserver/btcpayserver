using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.Eclair
{
    public class ChannelResponse
    {

        public string NodeId { get; set; }
        public string ChannelId { get; set; }
        public string State { get; set; }
    }
    public static class ChannelStates
    {
        public const string WAIT_FOR_FUNDING_CONFIRMED = "WAIT_FOR_FUNDING_CONFIRMED";

        public const string NORMAL = "NORMAL";
    }
}
