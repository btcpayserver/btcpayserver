using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using LedgerWallet;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class LedgerHardwareWalletService : HardwareWalletService
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
            public async Task<byte[][]> Exchange(byte[][] apdus, CancellationToken cancellationToken)
            {
                await _Semaphore.WaitAsync();
                List<byte[]> responses = new List<byte[]>();
                try
                {
                    foreach (var apdu in apdus)
                    {
                        await this.webSocket.SendAsync(new ArraySegment<byte>(apdu), WebSocketMessageType.Binary, true, cancellationToken);
                    }
                    foreach (var apdu in apdus)
                    {
                        byte[] response = new byte[300];
                        var result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(response), cancellationToken);
                        Array.Resize(ref response, result.Count);
                        responses.Add(response);
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

        public override string Device => "Ledger wallet";

        WebSocketTransport _Transport = null;
        public LedgerHardwareWalletService(System.Net.WebSockets.WebSocket ledgerWallet)
        {
            if (ledgerWallet == null)
                throw new ArgumentNullException(nameof(ledgerWallet));
            _Transport = new WebSocketTransport(ledgerWallet);
            _Ledger = new LedgerClient(_Transport);
            _Ledger.MaxAPDUSize = 90;
        }

        public override async Task<LedgerTestResult> Test(CancellationToken cancellation)
        {
            var version = await Ledger.GetFirmwareVersionAsync(cancellation);
            return new LedgerTestResult() { Success = true };
        }

        public override async Task<BitcoinExtPubKey> GetExtPubKey(BTCPayNetwork network, KeyPath keyPath, CancellationToken cancellation)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return await GetExtPubKey(network, keyPath, false, cancellation);
        }
        public override async Task<PubKey> GetPubKey(BTCPayNetwork network, KeyPath keyPath, CancellationToken cancellation)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return (await GetExtPubKey(network, keyPath, false, cancellation)).GetPublicKey();
        }

        private async Task<BitcoinExtPubKey> GetExtPubKey(BTCPayNetwork network, KeyPath account, bool onlyChaincode, CancellationToken cancellation)
        {
            var pubKey = await Ledger.GetWalletPubKeyAsync(account, cancellation: cancellation);
            try
            {
                pubKey.GetAddress(network.NBitcoinNetwork);
            }
            catch
            {
                if (network.NBitcoinNetwork.NetworkType == NetworkType.Mainnet)
                    throw new HardwareWalletException($"The opened ledger app does not seems to support {network.NBitcoinNetwork.Name}.");
            }
            var parentFP = onlyChaincode || account.Indexes.Length == 0 ? default : (await Ledger.GetWalletPubKeyAsync(account.Parent, cancellation: cancellation)).UncompressedPublicKey.Compress().GetHDFingerPrint();
            var extpubkey = new ExtPubKey(pubKey.UncompressedPublicKey.Compress(),
                                            pubKey.ChainCode,
                                            (byte)account.Indexes.Length,
                                            parentFP,
                                            account.Indexes.Length == 0 ? 0 : account.Indexes.Last()).GetWif(network.NBitcoinNetwork);
            return extpubkey;
        }

        public override async Task<PSBT> SignTransactionAsync(PSBT psbt, RootedKeyPath accountKeyPath, BitcoinExtPubKey accountKey, Script changeHint, CancellationToken cancellationToken)
        {
            var unsigned = psbt.GetGlobalTransaction();
            var changeKeyPath = psbt.Outputs.HDKeysFor(accountKey, accountKeyPath)
                                            .Where(o => changeHint == null ? true : changeHint == o.Coin.ScriptPubKey)
                                            .Select(o => o.RootedKeyPath.KeyPath)
                                            .FirstOrDefault();
            var signatureRequests = psbt
                .Inputs
                .HDKeysFor(accountKey, accountKeyPath)
                .Where(hd => !hd.Coin.PartialSigs.ContainsKey(hd.PubKey)) // Don't want to sign something twice
                .GroupBy(hd => hd.Coin.PrevOut, hd => hd)
                .Select(i => new SignatureRequest()
                {
                    InputCoin = i.First().Coin.GetSignableCoin(),
                    InputTransaction = i.First().Coin.NonWitnessUtxo,
                    KeyPath = i.First().RootedKeyPath.KeyPath,
                    PubKey = i.First().PubKey
                }).ToArray();
            await Ledger.SignTransactionAsync(signatureRequests, unsigned, changeKeyPath, cancellationToken);
            psbt = psbt.Clone();
            foreach (var signature in signatureRequests)
            {
                if (signature.Signature == null)
                    continue;
                var input = psbt.Inputs.FindIndexedInput(signature.InputCoin.Outpoint);
                if (input == null)
                    continue;
                input.PartialSigs.Add(signature.PubKey, signature.Signature);
            }
            return psbt;
        }

        public override void Dispose()
        {
            if (_Transport != null)
                _Transport.Dispose();
        }
    }
}
