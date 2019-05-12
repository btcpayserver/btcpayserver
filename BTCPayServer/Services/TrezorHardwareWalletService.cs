using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Device.Net;
using Hardwarewallets.Net.AddressManagement;
using LedgerWallet;
using NBitcoin;
using NBitpayClient;
using Trezor.Net;
using Trezor.Net.Contracts;
using Trezor.Net.Contracts.Bitcoin;
using Trezor.Net.Manager;

namespace BTCPayServer.Services
{
    public class TrezorHardwareWalletService : HardwareWalletService
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        class WebSocketTransport : IDevice
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
                        await this.webSocket.SendAsync(new ArraySegment<byte>(apdu), WebSocketMessageType.Binary, true,
                            cancellationToken);
                    }

                    foreach (var apdu in apdus)
                    {
                        byte[] response = new byte[300];
                        var result =
                            await this.webSocket.ReceiveAsync(new ArraySegment<byte>(response), cancellationToken);
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

            public async Task<byte[]> ReadAsync()
            {
                try
                {
                    byte[] response = new byte[300];
                    var result =
                        await this.webSocket.ReceiveAsync(new ArraySegment<byte>(response), CancellationToken.None);
                    Array.Resize(ref response, result.Count);
                    return response;
                }
                finally
                {
                    await _Semaphore.WaitAsync();
                }
            }

            public async Task WriteAsync(byte[] data)
            {
                try
                {
                    await this.webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true,
                        CancellationToken.None);
                }
                finally
                {
                    await _Semaphore.WaitAsync();
                }
            }

            public Task InitializeAsync()
            {
                return Task.CompletedTask;
            }

            public async Task<byte[]> WriteAndReadAsync(byte[] writeBuffer)
            {
                await WriteAsync(writeBuffer);
                return await ReadAsync();
            }

            public void Close()
            {
                _Semaphore.Dispose();
            }

            public bool IsInitialized { get; }
            public string DeviceId { get; }
            public ConnectedDeviceDefinitionBase ConnectedDeviceDefinition { get; }

            public void Dispose()
            {
                webSocket?.Dispose();
                _Semaphore?.Dispose();
            }
        }

        private readonly TrezorManager _trezor;
        public override string Device => "Ledger wallet";

        WebSocketTransport _Transport = null;

        public TrezorHardwareWalletService(System.Net.WebSockets.WebSocket trezorWallet, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            if (trezorWallet == null)
                throw new ArgumentNullException(nameof(trezorWallet));
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _Transport = new WebSocketTransport(trezorWallet);
            _trezor = new TrezorManager(() => Task.FromResult(string.Empty), () => Task.FromResult(string.Empty),
                _Transport);
        }

        public override async Task<LedgerTestResult> Test(CancellationToken cancellation)
        {
            
            return new LedgerTestResult() {Success = true};
        }

        public override async Task<BitcoinExtPubKey> GetExtPubKey(BTCPayNetwork network, KeyPath keyPath,
            CancellationToken cancellation)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return await GetExtPubKey(network, keyPath, false, cancellation);
        }

        public override async Task<PubKey> GetPubKey(BTCPayNetwork network, KeyPath keyPath,
            CancellationToken cancellation)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return (await GetExtPubKey(network, keyPath, false, cancellation)).GetPublicKey();
        }

        private async Task<BitcoinExtPubKey> GetExtPubKey(BTCPayNetwork network, KeyPath account, bool onlyChaincode,
            CancellationToken cancellation)
        {

            var coinInfo = _trezor.CoinUtility.GetCoinInfo(network.CoinType[0]);

            var response = await _trezor.SendMessageAsync<PublicKey, GetPublicKey>(new GetPublicKey()
            {
                CoinName = coinInfo.CoinName,
                ShowDisplay = false,
                ScriptType = InputScriptType.Spendwitness,
                
            });
            return new BitcoinExtPubKey(response.Xpub, network.NBitcoinNetwork);
            
            
        }
        
        public override async Task<PSBT> SignTransactionAsync(PSBT psbt, HDFingerprint? rootFingerprint, BitcoinExtPubKey accountKey, Script changeHint,
            CancellationToken cancellationToken)
        {
            var unsigned = psbt.GetGlobalTransaction();
            var changeKeyPath = psbt.Outputs
                .Where(o => changeHint == null ? true : changeHint == o.ScriptPubKey)
                .Where(o => o.HDKeyPaths.Any())
                .Select(o => o.HDKeyPaths.First().Value.Item2)
                .FirstOrDefault();
            var signatureRequests = psbt
                .Inputs
                .Where(o => o.HDKeyPaths.Any())
                .Where(o => !o.PartialSigs.ContainsKey(o.HDKeyPaths.First().Key))
                .Select(i => new TxAck.TransactionType.TxInputType()
                {
                   
                }).ToArray();
            
            
            //get address path for address in Trezor
            var addressPath = AddressPathBase.Parse<BIP44AddressPath>("m/49'/0'/0'/0/0").ToArray();

            // previous unspent input of Transaction
            var txInput = new TxAck.TransactionType.TxInputType()
            {
                AddressNs = addressPath,
                Amount = 100837,
                ScriptType = InputScriptType.Spendp2shwitness,
                PrevHash = "3becf448ae38cf08c0db3c6de2acb8e47acf6953331a466fca76165fdef1ccb7".ToHexBytes(), // transaction ID
                PrevIndex = 0,
                Sequence = 4294967293 // Sequence  number represent Replace By Fee 4294967293 or leave empty for default 
            };

            // TX we want to make a payment
            var txOut = new TxAck.TransactionType.TxOutputType()
            {
                AddressNs = new uint[0],
                Amount = 100837,
                Address = "18UxSJMw7D4UEiRqWkArN1Lq7VSGX6qH3H",
                ScriptType = TxAck.TransactionType.TxOutputType.OutputScriptType.Paytoaddress // if is segwit use Spendp2shwitness

            };

            // Must be filled with basic data like below
            var signTx = new SignTx()
            {
                Expiry = 0,
                LockTime = 0,
                CoinName = _trezor.CoinUtility.GetCoinInfo(_btcPayNetworkProvider.GetAll().Single(network => network.NBitcoinNetwork == accountKey.Network).CoinType[0]).CoinName,
                Version = 2,
                OutputsCount = (uint) psbt.Outputs.Count,
                InputsCount = (uint) signatureRequests.Length
            };

            // For every TX request from Trezor to us, we response with TxAck like below
            var txAck = new TxAck()
            {
                Tx = new TxAck.TransactionType()
                {
                    Inputs = { txInput }, // Tx Inputs
                    Outputs = { txOut },   // Tx Outputs
                    Expiry = 0,
                    InputsCnt = 1, // must be exact number of Inputs count
                    OutputsCnt = 1, // must be exact number of Outputs count
                    Version = 2
                }
            };

            // If the field serialized.serialized_tx from Trezor is set,
            // it contains a chunk of the signed transaction in serialized format.
            // The chunks are returned in the right order and just concatenating all returned chunks will result in the signed transaction.
            // So we need to add chunks to the list
            var serializedTx = new List<byte>();

            // We send SignTx() to the Trezor and we wait him to send us Request
            var request = await _trezor.SendMessageAsync<TxRequest, SignTx>(signTx);

            // We do loop here since we need to send over and over the same transactions to trezor because his 64 kilobytes memory
            // and he will sign chunks and return part of signed chunk in serialized manner, until we receive finall type of Txrequest TxFinished
            while (request.request_type != TxRequest.RequestType.Txfinished)
            {
                switch (request.request_type)
                {
                    case TxRequest.RequestType.Txinput:
                        {
                            //We send TxAck() with  TxInputs
                            request = await _trezor.SendMessageAsync<TxRequest, TxAck>(txAck);

                            // Now we have to check every response is there any SerializedTx chunk 
                            if (request.Serialized != null)
                            {
                                // if there is any we add to our list bytes
                                serializedTx.AddRange(request.Serialized.SerializedTx);
                            }

                            break;
                        }
                    case TxRequest.RequestType.Txoutput:
                        {
                            //We send TxAck()  with  TxOutputs
                            request = await _trezor.SendMessageAsync<TxRequest, object>(txAck);

                            // Now we have to check every response is there any SerializedTx chunk 
                            if (request.Serialized != null)
                            {
                                // if there is any we add to our list bytes
                                serializedTx.AddRange(request.Serialized.SerializedTx);
                            }

                            break;
                        }

                    case TxRequest.RequestType.Txextradata:
                        {
                            // for now he didn't ask me for extra data :)
                            break;
                        }
                    case TxRequest.RequestType.Txmeta:
                        {
                            // for now he didn't ask me for extra Tx meta data :)
                            break;
                        }
                }
            }
            
            
            psbt = psbt.Clone();
            foreach (var signature in signatureRequests)
            {
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
