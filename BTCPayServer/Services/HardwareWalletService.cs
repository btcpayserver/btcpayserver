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
    public class HardwareWalletService : IDisposable
    {
        class WebSocketTransport : LedgerWallet.Transports.ILedgerTransport, IDisposable
        {
            private readonly WebSocket webSocket;

            public WebSocketTransport(System.Net.WebSockets.WebSocket webSocket)
            {
                if (webSocket == null)
                    throw new ArgumentNullException(nameof(webSocket));
                this.webSocket = webSocket;
            }

            SemaphoreSlim _Semaphore = new SemaphoreSlim(1, 1);
            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
            public async Task<byte[][]> Exchange(byte[][] apdus)
            {
                await _Semaphore.WaitAsync();
                List<byte[]> responses = new List<byte[]>();
                try
                {
                    using (CancellationTokenSource cts = new CancellationTokenSource(Timeout))
                    {
                        foreach (var apdu in apdus)
                        {
                            await this.webSocket.SendAsync(new ArraySegment<byte>(apdu), WebSocketMessageType.Binary, true, cts.Token);
                        }
                        foreach (var apdu in apdus)
                        {
                            byte[] response = new byte[300];
                            var result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(response), cts.Token);
                            Array.Resize(ref response, result.Count);
                            responses.Add(response);
                        }
                    }
                }
                finally
                {
                    _Semaphore.Release();
                }
                return responses.ToArray();
            }

            public void Dispose()
            {
                _Semaphore.Dispose();
            }
        }

        private readonly LedgerClient _Ledger;
        public LedgerClient Ledger
        {
            get
            {
                return _Ledger;
            }
        }
        WebSocketTransport _Transport = null;
        public HardwareWalletService(System.Net.WebSockets.WebSocket ledgerWallet)
        {
            if (ledgerWallet == null)
                throw new ArgumentNullException(nameof(ledgerWallet));
            _Transport = new WebSocketTransport(ledgerWallet);
            _Ledger = new LedgerClient(_Transport);
        }

        public async Task<LedgerTestResult> Test()
        {
            var version = await _Ledger.GetFirmwareVersionAsync();
            return new LedgerTestResult() { Success = true };
        }

        public async Task<GetXPubResult> GetExtPubKey(BTCPayNetwork network, int account)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            var segwit = network.NBitcoinNetwork.Consensus.SupportSegwit;
            var path = network.GetRootKeyPath().Derive(account, true);
            var pubkey = await GetExtPubKey(_Ledger, network, path, false);
            var derivation = new DerivationStrategyFactory(network.NBitcoinNetwork).CreateDirectDerivationStrategy(pubkey, new DerivationStrategyOptions()
            {
                P2SH = segwit,
                Legacy = !segwit
            });
            return new GetXPubResult() { ExtPubKey = derivation.ToString(), KeyPath = path };
        }

        private static async Task<BitcoinExtPubKey> GetExtPubKey(LedgerClient ledger, BTCPayNetwork network, KeyPath account, bool onlyChaincode)
        {
            try
            {
                var pubKey = await ledger.GetWalletPubKeyAsync(account);
                try
                {
                    pubKey.GetAddress(network.NBitcoinNetwork);
                }
                catch
                {
                    if (network.NBitcoinNetwork.NetworkType == NetworkType.Mainnet)
                        throw new Exception($"The opened ledger app does not seems to support {network.NBitcoinNetwork.Name}.");
                }
                var fingerprint = onlyChaincode ? new byte[4] : (await ledger.GetWalletPubKeyAsync(account.Parent)).UncompressedPublicKey.Compress().Hash.ToBytes().Take(4).ToArray();
                var extpubkey = new ExtPubKey(pubKey.UncompressedPublicKey.Compress(), pubKey.ChainCode, (byte)account.Indexes.Length, fingerprint, account.Indexes.Last()).GetWif(network.NBitcoinNetwork);
                return extpubkey;
            }
            catch (FormatException)
            {
                throw new HardwareWalletException("Unsupported ledger app");
            }
        }

        public async Task<KeyPath> GetKeyPath(BTCPayNetwork network, DirectDerivationStrategy directStrategy)
        {
            List<KeyPath> derivations = new List<KeyPath>();
            if (network.NBitcoinNetwork.Consensus.SupportSegwit)
                derivations.Add(new KeyPath("49'"));
            derivations.Add(new KeyPath("44'"));
            KeyPath foundKeyPath = null;
            foreach (var account in
                                  derivations
                                  .Select(purpose => purpose.Derive(network.CoinType))
                                  .SelectMany(coinType => Enumerable.Range(0, 5).Select(i => coinType.Derive(i, true))))
            {
                try
                {
                    var extpubkey = await GetExtPubKey(_Ledger, network, account, true);
                    if (directStrategy.Root.PubKey == extpubkey.ExtPubKey.PubKey)
                    {
                        foundKeyPath = account;
                        break;
                    }
                }
                catch (FormatException)
                {
                    throw new Exception($"The opened ledger app does not support {network.NBitcoinNetwork.Name}");
                }
            }

            return foundKeyPath;
        }

        public async Task<Transaction> SignTransactionAsync(SignatureRequest[] signatureRequests,
                                                     Transaction unsigned,
                                                     KeyPath changeKeyPath)
        {
            _Transport.Timeout = TimeSpan.FromMinutes(5);
            return await Ledger.SignTransactionAsync(signatureRequests, unsigned, changeKeyPath);
        }

        public void Dispose()
        {
            if(_Transport != null)
                _Transport.Dispose();
        }
    }

    public class LedgerTestResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class GetXPubResult
    {
        public string ExtPubKey { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
    }
}
