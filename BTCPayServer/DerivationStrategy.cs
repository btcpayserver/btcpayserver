using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer
{
    public class DerivationStrategy : ISupportedPaymentMethod
    {
        private DerivationStrategyBase _DerivationStrategy;
        private BTCPayNetwork _Network;

        public DerivationStrategy(DerivationStrategyBase result, BTCPayNetwork network, bool enabled)
        {
            this._DerivationStrategy = result;
            this._Network = network;
            Enabled = enabled;
        }
        
        public static DerivationStrategy Parse(string derivationStrategy, BTCPayNetwork network, bool enabled)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var result = new NBXplorer.DerivationStrategy.DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationStrategy);
            return new DerivationStrategy(result, network, enabled);
        }

        public BTCPayNetwork Network { get { return this._Network; } }

        public DerivationStrategyBase DerivationStrategyBase => this._DerivationStrategy;

        public PaymentMethodId PaymentId => new PaymentMethodId(Network.CryptoCode, PaymentTypes.BTCLike);
        public bool Enabled { get; set; }

        public override string ToString()
        {
            return _DerivationStrategy.ToString();
        }
    }

    public class DerivationStrategyData
    {
        public string DerivationStrategy { get; set; }
        public bool Enabled { get; set; }
    }
}
