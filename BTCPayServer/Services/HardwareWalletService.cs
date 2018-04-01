using System;
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
    public class HardwareWalletService
    {
        class WebSocketTransport : LedgerWallet.Transports.ILedgerTransport
        {
            private readonly WebSocket webSocket;

            public WebSocketTransport(System.Net.WebSockets.WebSocket webSocket)
            {
                if (webSocket == null)
                    throw new ArgumentNullException(nameof(webSocket));
                this.webSocket = webSocket;
            }

            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
            public async Task<byte[][]> Exchange(byte[][] apdus)
            {
                List<byte[]> responses = new List<byte[]>();
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
                return responses.ToArray();
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

            var path = new KeyPath("49'").Derive(network.CoinType).Derive(account, true);
            var pubkey = await GetExtPubKey(_Ledger, network, path, false);
            var derivation = new DerivationStrategyFactory(network.NBitcoinNetwork).CreateDirectDerivationStrategy(pubkey, new DerivationStrategyOptions()
            {
                P2SH = true,
                Legacy = false
            });
            return new GetXPubResult() { ExtPubKey = derivation.ToString(), KeyPath = path };
        }

        private static async Task<BitcoinExtPubKey> GetExtPubKey(LedgerClient ledger, BTCPayNetwork network, KeyPath account, bool onlyChaincode)
        {
            try
            {
                var pubKey = await ledger.GetWalletPubKeyAsync(account);
                if (pubKey.Address.Network != network.NBitcoinNetwork)
                {
                    if (network.DefaultSettings.ChainType == NBXplorer.ChainType.Main)
                        throw new Exception($"The opened ledger app should be for {network.NBitcoinNetwork.Name}, not for {pubKey.Address.Network}");
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

        public async Task<bool> SupportDerivation(BTCPayNetwork network, DirectDerivationStrategy strategy)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (strategy == null)
                throw new ArgumentNullException(nameof(strategy));
            if (!strategy.Segwit)
                return false;
            return await GetKeyPath(_Ledger, network, strategy) != null;
        }

        private static async Task<KeyPath> GetKeyPath(LedgerClient ledger, BTCPayNetwork network, DirectDerivationStrategy directStrategy)
        {
            KeyPath foundKeyPath = null;
            foreach (var account in
                                  new[] { new KeyPath("49'"), new KeyPath("44'") }
                                  .Select(purpose => purpose.Derive(network.CoinType))
                                  .SelectMany(coinType => Enumerable.Range(0, 5).Select(i => coinType.Derive(i, true))))
            {
                try
                {
                    var extpubkey = await GetExtPubKey(ledger, network, account, true);
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

        public async Task<Transaction> SendToAddress(DirectDerivationStrategy strategy, 
                                                             ReceivedCoin[] coins, BTCPayNetwork network, 
                                                             (IDestination destination, Money amount, bool substractFees)[] send, 
                                                             FeeRate feeRate, 
                                                             IDestination changeAddress,
                                                             KeyPath changeKeyPath,
                                                             FeeRate minTxRelayFee)
        {
            if (strategy == null)
                throw new ArgumentNullException(nameof(strategy));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (feeRate == null)
                throw new ArgumentNullException(nameof(feeRate));
            if (changeAddress == null)
                throw new ArgumentNullException(nameof(changeAddress));
            if (feeRate.FeePerK <= Money.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(feeRate), "The fee rate should be above zero");
            }

            foreach (var element in send)
            {
                if (element.destination == null)
                    throw new ArgumentNullException(nameof(element.destination));
                if (element.amount == null)
                    throw new ArgumentNullException(nameof(element.amount));
                if (element.amount <= Money.Zero)
                    throw new ArgumentOutOfRangeException(nameof(element.amount), "The amount should be above zero");
            }

            var foundKeyPath = await GetKeyPath(Ledger, network, strategy);

            if (foundKeyPath == null)
            {
                throw new HardwareWalletException($"This store is not configured to use this ledger");
            }

            TransactionBuilder builder = new TransactionBuilder();
            builder.StandardTransactionPolicy.MinRelayTxFee = minTxRelayFee;
            builder.AddCoins(coins.Select(c=>c.Coin).ToArray());

            foreach (var element in send)
            {
                builder.Send(element.destination, element.amount);
                if (element.substractFees)
                    builder.SubtractFees();
            }
            builder.SetChange(changeAddress);
            builder.SendEstimatedFees(feeRate);
            builder.Shuffle();
            var unsigned = builder.BuildTransaction(false);

            var keypaths = new Dictionary<Script, KeyPath>();
            foreach(var c in coins)
            {
                keypaths.TryAdd(c.Coin.ScriptPubKey, c.KeyPath);
            }

            var hasChange = unsigned.Outputs.Count == 2;
            var usedCoins = builder.FindSpentCoins(unsigned);
            _Transport.Timeout = TimeSpan.FromMinutes(5);
            var fullySigned = await Ledger.SignTransactionAsync(
                usedCoins.Select(c => new SignatureRequest
                {
                    InputCoin = c,
                    KeyPath = foundKeyPath.Derive(keypaths[c.TxOut.ScriptPubKey]),
                    PubKey = strategy.Root.Derive(keypaths[c.TxOut.ScriptPubKey]).PubKey
                }).ToArray(),
                unsigned,
                hasChange ? foundKeyPath.Derive(changeKeyPath) : null);
            return fullySigned;
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
        public int CoinType { get; internal set; }
    }
}
