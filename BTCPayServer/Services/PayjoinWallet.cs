using NBitcoin;
using NBitcoin.BIP78.Client;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.Services
{
    public class PayjoinWallet : IPayjoinWallet
    {
        private readonly DerivationSchemeSettings _derivationSchemeSettings;

        public PayjoinWallet(DerivationSchemeSettings derivationSchemeSettings)
        {
            _derivationSchemeSettings = derivationSchemeSettings;
        }
        public IHDScriptPubKey Derive(KeyPath keyPath)
        {
            return ((IHDScriptPubKey)_derivationSchemeSettings.AccountDerivation).Derive(keyPath);
        }

        public bool CanDeriveHardenedPath()
        {
            return _derivationSchemeSettings.AccountDerivation.CanDeriveHardenedPath();
        }

        public Script ScriptPubKey => ((IHDScriptPubKey)_derivationSchemeSettings.AccountDerivation).ScriptPubKey;
        public ScriptPubKeyType ScriptPubKeyType => _derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();

        public RootedKeyPath RootedKeyPath =>
            _derivationSchemeSettings.GetSigningAccountKeySettings().GetRootedKeyPath();

        public IHDKey AccountKey => _derivationSchemeSettings.GetSigningAccountKeySettings().AccountKey;
    }
}
