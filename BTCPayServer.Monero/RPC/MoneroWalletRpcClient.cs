using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Monero.RPC.Models;

namespace BTCPayServer.Monero.RPC
{
    public class MoneroWalletRpcClient : BaseRPCClient
    {
        public MoneroWalletRpcClient(Uri address, string username, string password, HttpClient client = null) : base(address, username, password, client)
        {
        }
        
        public Task<GetBalanceResponse> GetBalance(GetBalanceRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetBalanceRequest, GetBalanceResponse>("get_balance", request, cts);
        }
        public Task<GetAddressResponse> GetAddress(GetAddressRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetAddressRequest, GetAddressResponse>("create_address", request, cts);
        }
        
        public Task<CreateAddressResponse> CreateAddress(CreateAddressRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", request, cts);
        }
        public Task<GetAccountsResponse> GetAccounts(GetAccountsRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", request, cts);
        }
        public Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", request, cts);
        }
        public Task<GetHeightResponse> GetHeight(CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<NoRequestModel, GetHeightResponse>("get_height", NoRequestModel.Instance, cts);
        }
        public Task<IncomingTransfersResponse> IncomingTransfers(IncomingTransfersRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<IncomingTransfersRequest, IncomingTransfersResponse>("incoming_transfers", request, cts);
        }
        public Task<MakeUriResponse> MakeUri(MakeUriRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<MakeUriRequest, MakeUriResponse>("make_uri", request, cts);
        }
        public Task<GetTransfersResponse> GetTransfers(GetTransfersRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetTransfersRequest, GetTransfersResponse>("get_transfers", request, cts);
        }
        public Task<GetTransferByTransactionIdResponse> GetTransferByTransactionId(GetTransferByTransactionIdRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetTransferByTransactionIdRequest, GetTransferByTransactionIdResponse>("get_transfer_by_txid", request, cts);
        }        
        public Task<GetAddressIndexResponse> GetAddressIndex(GetAddressIndexRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetAddressIndexRequest, GetAddressIndexResponse>("get_address_index", request, cts);
        }
    }
}
