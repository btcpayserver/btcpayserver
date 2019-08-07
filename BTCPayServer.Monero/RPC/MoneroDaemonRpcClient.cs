using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Monero.RPC.Models;

namespace BTCPayServer.Monero.RPC
{
    public class MoneroDaemonRpcClient : BaseRPCClient
    {
        public MoneroDaemonRpcClient(Uri address, HttpClient client = null) : base(address, "", "", client)
        {
        }
        
        public Task<GetBlockCountResponse> GetBlockCount(CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<NoRequestModel, GetBlockCountResponse>("get_block_count", NoRequestModel.Instance, cts);
        }
        public Task<string> OnGetBlockHash(int blockHeight, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<int[], string>("on_getblockhash", new [] {blockHeight}, cts);
        }
        
        public Task<GetBlockTemplateResponse> GetBlockTemplate(GetBlockTemplateRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetBlockTemplateRequest, GetBlockTemplateResponse>("get_block_template", request, cts);
        }
        public Task SubmitBlock(string[] blockBlobsbData, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<string[], StatusResponse>("submit_block", blockBlobsbData, cts);
        }
       
        public Task<GetInfoResponse> GetInfo(CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<NoRequestModel, GetInfoResponse>("get_info", NoRequestModel.Instance, cts);
        } 
        public Task<GetLastBlockHeaderResponse> GetLastBlockHeader(CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<NoRequestModel, GetLastBlockHeaderResponse>("get_last_block_header", NoRequestModel.Instance, cts);
        }

        public Task<GetBlockHeaderByHashResponse> GetBlockHeaderByHash(GetBlockTemplateRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetBlockTemplateRequest, GetBlockHeaderByHashResponse>("get_block_header_by_hash", request, cts);
        }
        
        public Task<GetBlockHeaderByHeightResponse> GetBlockHeaderByHeight(GetBlockHeaderByHeightRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetBlockHeaderByHeightRequest, GetBlockHeaderByHeightResponse>("get_block_header_by_height", request, cts);
        }        
        public Task<GetFeeEstimateResponse> GetFeeEstimate(GetFeeEstimateRequest request, CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", request, cts);
        }
        
        public Task<SyncInfoResponse> SyncInfo(CancellationToken cts = default(CancellationToken))
        {
            return SendCommandAsync<NoRequestModel, SyncInfoResponse>("sync_info", NoRequestModel.Instance, cts);
        }
    }
}
