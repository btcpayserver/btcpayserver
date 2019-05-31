using System.Collections.Generic;
using J = Newtonsoft.Json.JsonPropertyAttribute;

namespace BTCPayServer.Monero.RPC.Models
{
    public class GetInfoResponse
    {
        [J("alt_blocks_count")] public long AltBlocksCount { get; set; }
        [J("block_size_limit")] public long BlockSizeLimit { get; set; }
        [J("block_size_median")] public long BlockSizeMedian { get; set; }
        [J("bootstrap_daemon_address")] public string BootstrapDaemonAddress { get; set; }
        [J("cumulative_difficulty")] public decimal CumulativeDifficulty { get; set; }
        [J("difficulty")] public long Difficulty { get; set; }
        [J("free_space")] public long FreeSpace { get; set; }
        [J("grey_peerlist_size")] public long GreyPeerlistSize { get; set; }
        [J("height")] public long Height { get; set; }
        [J("height_without_bootstrap")] public long HeightWithoutBootstrap { get; set; }
        [J("incoming_connections_count")] public long IncomingConnectionsCount { get; set; }
        [J("mainnet")] public bool Mainnet { get; set; }
        [J("offline")] public bool Offline { get; set; }
        [J("outgoing_connections_count")] public long OutgoingConnectionsCount { get; set; }
        [J("rpc_connections_count")] public long RpcConnectionsCount { get; set; }
        [J("stagenet")] public bool Stagenet { get; set; }
        [J("start_time")] public long StartTime { get; set; }
        [J("status")] public string Status { get; set; }
        [J("target")] public long Target { get; set; }
        [J("target_height")] public long TargetHeight { get; set; }
        [J("testnet")] public bool Testnet { get; set; }
        [J("top_block_hash")] public string TopBlockHash { get; set; }
        [J("tx_count")] public long TxCount { get; set; }
        [J("tx_pool_size")] public long TxPoolSize { get; set; }
        [J("untrusted")] public bool Untrusted { get; set; }
        [J("was_bootstrap_ever_used")] public bool WasBootstrapEverUsed { get; set; }
        [J("white_peerlist_size")] public long WhitePeerlistSize { get; set; }
    }

    public partial class GetTransfersRequest
    {
        [J("in")] public bool In { get; set; }
        [J("out")] public bool Out { get; set; }
        [J("pending")] public bool Pending { get; set; }
        [J("failed")] public bool Failed { get; set; }
        [J("pool")] public bool Pool { get; set; }
        [J("filter_by_height ")] public bool FilterByHeight { get; set; }
        [J("min_height")] public long MinHeight { get; set; }
        [J("max_height")] public long MaxHeight { get; set; }
        [J("account_index")] public long AccountIndex { get; set; }
        [J("subaddr_indices")] public List<long> SubaddrIndices { get; set; }
    }

    public class GetTransferByTransactionIdRequest
    {
        [J("txid")] public string TransactionId { get; set; }

        [J("account_index")] public long AccountIndex { get; set; }
    }
    
    public partial class GetAddressRequest
    {
        [J("account_index")] public long AccountIndex { get; set; }      
        [J("address_index")] public List<long> AddressIndex { get; set; }
    }
    
    public partial class GetAddressResponse
    {
        [J("address")]   public string Address { get; set; }         
        [J("addresses")] public List<Address> Addresses { get; set; }
    }

    public partial class Address
    {
        [J("address")]       public string AddressAddress { get; set; }
        [J("address_index")] public long AddressIndex { get; set; }    
        [J("label")]         public string Label { get; set; }         
        [J("used")]          public bool Used { get; set; }            
    }
}
