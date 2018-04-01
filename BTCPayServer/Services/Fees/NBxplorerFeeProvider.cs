using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProviderFactory : IFeeProviderFactory
    {
        public NBXplorerFeeProviderFactory(ExplorerClientProvider explorerClients)
        {
            if (explorerClients == null)
                throw new ArgumentNullException(nameof(explorerClients));
            _ExplorerClients = explorerClients;
        }

        private readonly ExplorerClientProvider _ExplorerClients;

        public FeeRate Fallback { get; set; }
        public int BlockTarget { get; set; }
        public IFeeProvider CreateFeeProvider(BTCPayNetwork network)
        {
            return new NBXplorerFeeProvider(this, _ExplorerClients.GetExplorerClient(network));
        }
    }
    public class NBXplorerFeeProvider : IFeeProvider
    {
        public NBXplorerFeeProvider(NBXplorerFeeProviderFactory parent, ExplorerClient explorerClient)
        {
            if (explorerClient == null)
                throw new ArgumentNullException(nameof(explorerClient));
            _Factory = parent;
            _ExplorerClient = explorerClient;
        }
        NBXplorerFeeProviderFactory _Factory;
        ExplorerClient _ExplorerClient;
        public async Task<FeeRate> GetFeeRateAsync()
        {
            try
            {
                return (await _ExplorerClient.GetFeeRateAsync(_Factory.BlockTarget).ConfigureAwait(false)).FeeRate;
            }
            catch (NBXplorerException ex) when (ex.Error.HttpCode == 400 && ex.Error.Code == "fee-estimation-unavailable")
            {
                return _Factory.Fallback;
            }
        }
    }
}
