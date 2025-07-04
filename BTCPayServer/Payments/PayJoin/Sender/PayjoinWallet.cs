using System.Linq;
using BTCPayServer.BIP78.Sender;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin.Sender
{
    public class PayjoinWallet : IPayjoinWallet
    {
        private readonly DerivationSchemeSettings _derivationSchemeSettings;

        public PayjoinWallet(DerivationSchemeSettings derivationSchemeSettings)
        {
            _derivationSchemeSettings = derivationSchemeSettings;
            AccountSettings = _derivationSchemeSettings.AccountKeySettings.Single();
        }

        public AccountKeySettings AccountSettings { get; set; }

        public IHDScriptPubKey Derive(KeyPath keyPath)
        {
            return ((IHDScriptPubKey)_derivationSchemeSettings.AccountDerivation).Derive(keyPath);
        }

        public Script ScriptPubKey => ((IHDScriptPubKey)_derivationSchemeSettings.AccountDerivation).ScriptPubKey;
        public ScriptPubKeyType ScriptPubKeyType => _derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();

        public RootedKeyPath RootedKeyPath =>
            AccountSettings.GetRootedKeyPath();

        public IHDKey AccountKey => AccountSettings.AccountKey;
    }
}
