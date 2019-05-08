using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public class DerivationSchemeSettings : ISupportedPaymentMethod
    {
        public static DerivationSchemeSettings Parse(string derivationStrategy, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var result = new NBXplorer.DerivationStrategy.DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationStrategy);
            return new DerivationSchemeSettings(result, network) { AccountOriginal = derivationStrategy.Trim() };
        }
        
        public DerivationSchemeSettings()
        {

        }
        public DerivationSchemeSettings(DerivationStrategyBase derivationStrategy, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            AccountDerivation = derivationStrategy;
            Network = network;
        }
        [JsonIgnore]
        public BTCPayNetwork Network { get; set; }

        public KeyPath AccountKeyPath { get; set; }
        
        public DerivationStrategyBase AccountDerivation { get; set; }
        public string AccountOriginal { get; set; }

        public HDFingerprint? RootFingerprint { get; set; }

        public string Label { get; set; }

        public PaymentMethodId PaymentId => new PaymentMethodId(Network.CryptoCode, PaymentTypes.BTCLike);

        public override string ToString()
        {
            return AccountDerivation.ToString();
        }
        public string ToPrettyString()
        {
            return string.IsNullOrEmpty(Label) ? Label :
                   String.IsNullOrEmpty(AccountOriginal) ? AccountOriginal :
                   ToString();
        }
    }
}
