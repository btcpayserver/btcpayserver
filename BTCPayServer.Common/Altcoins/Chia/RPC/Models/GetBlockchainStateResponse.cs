using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class GetBlockchainStateResponse
    {
        [JsonProperty("blockchain_state")] public BlockchainStateEntry BlockchainState { get; set; }

        public partial class BlockchainStateEntry
        {
            [JsonProperty("node_id")] public string NodeId { get; set; }

            [JsonProperty("difficulty")] public int Difficulty { get; set; }

            [JsonProperty("genesis_challenge_initialized")]
            public bool GenesisChallengeInitialized { get; set; }

            [JsonProperty("mempool_size")] public int MempoolSize { get; set; }

            [JsonProperty("mempool_cost")] public BigInteger MempoolCost { get; set; }

            [JsonProperty("mempool_fees")] public BigInteger MempoolFees { get; set; }

            [JsonProperty("mempool_min_fees")] public Dictionary<string, BigInteger> MempoolMinFees { get; set; }

            [JsonProperty("mempool_max_total_cost")]
            public BigInteger MempoolMaxTotalCost { get; set; }

            [JsonProperty("block_max_cost")] public BigInteger BlockMaxCost { get; set; }

            [JsonProperty("peak")] public Peak Peak { get; set; }

            [JsonProperty("space")] public BigInteger Space { get; set; }

            [JsonProperty("sub_slot_iters")] public int SubSlotIters { get; set; }

            [JsonProperty("average_block_time")] public int AverageBlockTime { get; set; }

            [JsonProperty("sync")] public Sync Sync { get; set; }
        }

        public partial class Peak
        {
            [JsonProperty("height")] public int Height { get; set; }
        }

        public partial class Sync
        {
            [JsonProperty("sync_mode")] public bool SyncMode { get; set; }

            [JsonProperty("sync_progress_height")] public int SyncProgressHeight { get; set; }

            [JsonProperty("sync_tip_height")] public int SyncTipHeight { get; set; }

            [JsonProperty("synced")] public bool Synced { get; set; }
        }
    }
}
