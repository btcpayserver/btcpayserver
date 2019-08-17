using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Wallets;
using LedgerWallet;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{

    public class HardwareWalletException : Exception
    {
        public HardwareWalletException() { }
        public HardwareWalletException(string message) : base(message) { }
        public HardwareWalletException(string message, Exception inner) : base(message, inner) { }
    }
    public abstract class HardwareWalletService : IDisposable
    {
        public abstract string Device { get; }
        public abstract Task<LedgerTestResult> Test(CancellationToken cancellation);

        public abstract Task<BitcoinExtPubKey> GetExtPubKey(BTCPayNetwork network, KeyPath keyPath, CancellationToken cancellation);
        public virtual async Task<PubKey> GetPubKey(BTCPayNetwork network, KeyPath keyPath, CancellationToken cancellation)
        {
            return (await GetExtPubKey(network, keyPath, cancellation)).GetPublicKey();
        }

        public async Task<KeyPath> FindKeyPathFromDerivation(BTCPayNetwork network, DerivationStrategyBase derivationScheme, CancellationToken cancellation)
        {
            var pubKeys = derivationScheme.GetExtPubKeys().Select(k => k.GetPublicKey()).ToArray();
            var derivation = derivationScheme.GetDerivation(new KeyPath(0));
            List<KeyPath> derivations = new List<KeyPath>();
            if (network.NBitcoinNetwork.Consensus.SupportSegwit)
            {
                if (derivation.Redeem?.IsScriptType(ScriptType.Witness) is true ||
                    derivation.ScriptPubKey.IsScriptType(ScriptType.Witness)) // Native or p2sh segwit
                    derivations.Add(new KeyPath("49'"));
                if (derivation.Redeem == null && derivation.ScriptPubKey.IsScriptType(ScriptType.Witness)) // Native segwit
                    derivations.Add(new KeyPath("84'"));
            }
            derivations.Add(new KeyPath("44'"));
            KeyPath foundKeyPath = null;
            foreach (var account in
                                  derivations
                                  .Select(purpose => purpose.Derive(network.CoinType))
                                  .SelectMany(coinType => Enumerable.Range(0, 5).Select(i => coinType.Derive(i, true))))
            {
                var pubkey = await GetPubKey(network, account, cancellation);
                if (pubKeys.Contains(pubkey))
                {
                    foundKeyPath = account;
                    break;
                }
            }

            return foundKeyPath;
        }

        public abstract Task<PSBT> SignTransactionAsync(PSBT psbt, RootedKeyPath accountKeyPath, BitcoinExtPubKey accountKey, Script changeHint, CancellationToken cancellationToken);

        public virtual void Dispose()
        {
        }
    }

    public class LedgerTestResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }
}
