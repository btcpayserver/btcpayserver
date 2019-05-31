using System.Collections.Generic;
using Newtonsoft.Json;
using J = Newtonsoft.Json.JsonPropertyAttribute;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetBlockHeaderByHeightResponse
    {
        [JsonProperty("block_header")] public BlockHeader BlockHeader { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("untrusted")] public bool Untrusted { get; set; }
    }

    public partial class GetBalanceRequest
    {
        [J("account_index")] public long AccountIndex { get; set; }
        [J("address_indices")] public List<long> AddressIndices { get; set; }
    }

    public partial class GetBalanceResponse
    {
        [J("balance")] public decimal Balance { get; set; }
        [J("multisig_import_needed")] public bool MultisigImportNeeded { get; set; }
        [J("per_subaddress")] public List<PerSubaddress> PerSubaddress { get; set; }
        [J("unlocked_balance")] public decimal UnlockedBalance { get; set; }
    }

    public partial class PerSubaddress
    {
        [J("address")] public string Address { get; set; }
        [J("address_index")] public long AddressIndex { get; set; }
        [J("balance")] public decimal Balance { get; set; }
        [J("label")] public string Label { get; set; }
        [J("num_unspent_outputs")] public long NumUnspentOutputs { get; set; }
        [J("unlocked_balance")] public decimal UnlockedBalance { get; set; }
    }

    public partial class CreateAddressRequest
    {
        [J("account_index")] public long AccountIndex { get; set; }
        [J("label")] public string Label { get; set; }
    }

    public partial class CreateAddressResponse
    {
        [J("address")] public string Address { get; set; }
        [J("address_index")] public long AddressIndex { get; set; }
    }

    public partial class CreateAddressRequest
    {
        [J("tag")] public string Tag { get; set; }
    }

    public partial class GetAccountsRequest
    {
        [J("tag")] public string Tag { get; set; }
    }

    public partial class GetAccountsResponse
    {
        [J("subaddress_accounts")] public List<SubaddressAccount> SubaddressAccounts { get; set; }
        [J("total_balance")] public decimal TotalBalance { get; set; }
        [J("total_unlocked_balance")] public decimal TotalUnlockedBalance { get; set; }
    }

    public partial class SubaddressAccount
    {
        [J("account_index")] public long AccountIndex { get; set; }
        [J("balance")] public decimal Balance { get; set; }
        [J("base_address")] public string BaseAddress { get; set; }
        [J("label")] public string Label { get; set; }
        [J("tag")] public string Tag { get; set; }
        [J("unlocked_balance")] public decimal UnlockedBalance { get; set; }
    }

    public partial class CreateAccountRequest
    {
        [J("label")] public string Label { get; set; }
    }

    public partial class CreateAccountResponse
    {
        [J("account_index")] public long AccountIndex { get; set; }
        [J("address")] public string Address { get; set; }
    }

    public partial class GetHeightResponse
    {
        [J("height")] public long Height { get; set; }
    }

    public partial class IncomingTransfersRequest
    {
        [J("transfer_type")] public string TransferType { get; set; }
        [J("account_index")] public long AccountIndex { get; set; }
        [J("subaddr_indices")] public List<long> SubaddrIndices { get; set; }
        [J("verbose")] public bool Verbose { get; set; }
    }

    public partial class IncomingTransfersResponse
    {
        [J("transfers")] public List<Transfer> Transfers { get; set; }
    }

    public partial class Transfer
    {
        [J("amount")] public long Amount { get; set; }
        [J("global_index")] public long GlobalIndex { get; set; }
        [J("key_image")] public string KeyImage { get; set; }
        [J("spent")] public bool Spent { get; set; }
        [J("subaddr_index")] public long SubaddrIndex { get; set; }
        [J("tx_hash")] public string TxHash { get; set; }
        [J("tx_size")] public long TxSize { get; set; }
    }


    public partial class MakeUriRequest
    {
        [J("address")] public string Address { get; set; }
        [J("amount")] public long Amount { get; set; }
        [J("payment_id")] public string PaymentId { get; set; }
        [J("tx_description")] public string TxDescription { get; set; }
        [J("recipient_name")] public string RecipientName { get; set; }
    }

    public partial class MakeUriResponse
    {
        [J("uri")] public string Uri { get; set; }
    }

    public partial class GetTransfersResponse
    {
        [J("in")] public List<GetTransfersResponseItem> In { get; set; }
        [J("out")] public List<GetTransfersResponseItem> Out { get; set; }
        [J("pending")] public List<GetTransfersResponseItem> Pending { get; set; }
        [J("failed")] public List<GetTransfersResponseItem> Failed { get; set; }
        [J("pool")] public List<GetTransfersResponseItem> Pool { get; set; }

        public partial class GetTransfersResponseItem

        {
            [J("address")] public string Address { get; set; }
            [J("amount")] public long Amount { get; set; }
            [J("confirmations")] public long Confirmations { get; set; }
            [J("double_spend_seen")] public bool DoubleSpendSeen { get; set; }
            [J("fee")] public long Fee { get; set; }
            [J("height")] public long Height { get; set; }
            [J("note")] public string Note { get; set; }
            [J("payment_id")] public string PaymentId { get; set; }
            [J("subaddr_index")] public SubaddrIndex SubaddrIndex { get; set; }

            [J("suggested_confirmations_threshold")]
            public long SuggestedConfirmationsThreshold { get; set; }

            [J("timestamp")] public long Timestamp { get; set; }
            [J("txid")] public string Txid { get; set; }
            [J("type")] public string Type { get; set; }
            [J("unlock_time")] public long UnlockTime { get; set; }
        }
    }


    public partial class GetTransferByTransactionIdResponse
    {
        [J("transfer")] public TransferItem Transfer { get; set; }

        public partial class TransferItem
        {
            [J("address")] public string Address { get; set; }
            [J("amount")] public long Amount { get; set; }
            [J("confirmations")] public long Confirmations { get; set; }
            [J("destinations")] public List<Destination> Destinations { get; set; }
            [J("double_spend_seen")] public bool DoubleSpendSeen { get; set; }
            [J("fee")] public long Fee { get; set; }
            [J("height")] public long Height { get; set; }
            [J("note")] public string Note { get; set; }
            [J("payment_id")] public string PaymentId { get; set; }
            [J("subaddr_index")] public SubaddrIndex SubaddrIndex { get; set; }

            [J("suggested_confirmations_threshold")]
            public long SuggestedConfirmationsThreshold { get; set; }

            [J("timestamp")] public long Timestamp { get; set; }
            [J("txid")] public string Txid { get; set; }
            [J("type")] public string Type { get; set; }
            [J("unlock_time")] public long UnlockTime { get; set; }
        }
    }

    public partial class Destination
    {
        [J("address")] public string Address { get; set; }
        [J("amount")] public long Amount { get; set; }
    }

    public partial class SubaddrIndex
    {
        [J("major")] public long Major { get; set; }
        [J("minor")] public long Minor { get; set; }
    }   
    public partial class GetAddressIndexResponse
    {
        [J("index")] public SubaddrIndex Index { get; set; }
    }    
    public partial class GetAddressIndexRequest
    {
        [J("address")] public string Address { get; set; }
    }
    
    
}
