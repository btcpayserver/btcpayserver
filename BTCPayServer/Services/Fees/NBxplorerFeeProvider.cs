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
        public NBXplorerFeeProviderFactory(ExplorerClient explorerClient)
        {
            if (explorerClient == null)
                throw new ArgumentNullException(nameof(explorerClient));
            _ExplorerClient = explorerClient;
        }

        private readonly ExplorerClient _ExplorerClient;
        public ExplorerClient ExplorerClient
        {
            get
            {
                return _ExplorerClient;
            }
        }

        public FeeRate Fallback { get; set; }
        public int BlockTarget { get; set; }
        public IFeeProvider CreateFeeProvider(BTCPayNetwork network)
        {
            return new NBXplorerFeeProvider(this);
        }
    }
    public class NBXplorerFeeProvider : IFeeProvider
    {
        public NBXplorerFeeProvider(NBXplorerFeeProviderFactory factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _Factory = factory;
        }
        private readonly NBXplorerFeeProviderFactory _Factory;
        public async Task<FeeRate> GetFeeRateAsync()
        {
            try
            {
                return (await _Factory.ExplorerClient.GetFeeRateAsync(_Factory.BlockTarget).ConfigureAwait(false)).FeeRate;
            }
            catch (NBXplorerException ex) when (ex.Error.HttpCode == 400 && ex.Error.Code == "fee-estimation-unavailable")
            {
                return _Factory.Fallback;
            }
        }
    }
}
