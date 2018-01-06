using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer
{
    public class DerivationStrategy
    {
        private DerivationStrategyBase _DerivationStrategy;
        private BTCPayNetwork _Network;

        DerivationStrategy(DerivationStrategyBase result, BTCPayNetwork network)
        {
            this._DerivationStrategy = result;
            this._Network = network;
        }
        
        public static DerivationStrategy Parse(string derivationStrategy, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var result = new NBXplorer.DerivationStrategy.DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationStrategy);
            return new DerivationStrategy(result, network);
        }

        public BTCPayNetwork Network { get { return this._Network; } }

        public DerivationStrategyBase DerivationStrategyBase { get { return this._DerivationStrategy; } }

        public override string ToString()
        {
            return _DerivationStrategy.ToString();
        }
    }
}
