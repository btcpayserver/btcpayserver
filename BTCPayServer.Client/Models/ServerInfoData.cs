using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class ServerInfoData
    {
        /// <summary>
        /// detailed status information
        /// </summary>
        public ServerInfoStatusData Status { get; set; }
        
        /// <summary>
        /// the BTCPay Server version
        /// </summary>
        public string Version { get; set; }
        
        /// <summary>
        /// the Tor hostname
        /// </summary>
        public string Onion { get; set; }
        
        /// <summary>
        /// the payment methods this server supports
        /// </summary>
        public IEnumerable<string> SupportedPaymentMethods { get; set; }
    }

    public class ServerInfoStatusData
    {
        /// <summary>
        /// are all chains fully synched
        /// </summary>
        public bool FullySynched { get; set; }
        
        /// <summary>
        /// detailed sync information per chain
        /// </summary>
        public IEnumerable<ServerInfoSyncStatusData> SyncStatus { get; set; }
    }

    public class ServerInfoSyncStatusData
    {
        public string CryptoCode { get; set; }
        public int BlockHeaders { get; set; }
        public double Progress { get; set; } 
    }
}
