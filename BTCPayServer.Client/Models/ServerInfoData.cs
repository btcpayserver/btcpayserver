using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class ServerInfoData
    {
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

        /// <summary>
        /// are all chains fully synched
        /// </summary>
        public bool FullySynched { get; set; }

        /// <summary>
        /// detailed sync information per chain
        /// </summary>
        public IEnumerable<SyncStatus> SyncStatus { get; set; }
    }

    public class SyncStatus
    {
        public string PaymentMethodId { get; set; }
        public virtual bool Available { get; set; }
    }

    public class ServerInfoSyncStatusData : SyncStatus
    {
        public int ChainHeight { get; set; }
        public int? SyncHeight { get; set; }
        public ServerInfoNodeData NodeInformation { get; set; }
    }

    public class ServerInfoNodeData
    {
        public int Headers { get; set; }
        public int Blocks { get; set; }
        public double VerificationProgress { get; set; }
    }
}
