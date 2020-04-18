using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Http;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IHttpClientFactory = System.Net.Http.IHttpClientFactory;

namespace BTCPayServer.Services
{

    public static class PSBTExtensions
    {
        public static ScriptPubKeyType? GetInputsScriptPubKeyType(this PSBT psbt)
        {
            if (!psbt.IsAllFinalized() || psbt.Inputs.Any(i => i.WitnessUtxo == null))
                throw new InvalidOperationException("The psbt should be finalized with witness information");
            var coinsPerTypes = psbt.Inputs.Select(i =>
            {
                return ((PSBTCoin)i, i.GetInputScriptPubKeyType());
            }).GroupBy(o => o.Item2, o => o.Item1).ToArray();
            if (coinsPerTypes.Length != 1)
                return default;
            return coinsPerTypes[0].Key;
        }

        public static ScriptPubKeyType? GetInputScriptPubKeyType(this PSBTInput i)
        {
            if (i.WitnessUtxo.ScriptPubKey.IsScriptType(ScriptType.P2WPKH))
                return ScriptPubKeyType.Segwit;
            if (i.WitnessUtxo.ScriptPubKey.IsScriptType(ScriptType.P2SH) &&
                PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(i.FinalScriptWitness) is {})
                return ScriptPubKeyType.SegwitP2SH;
            return null;
        }
    }

    public class PayjoinClient
    {
        public const string PayjoinOnionNamedClient = "payjoin.onion";
        public const string PayjoinClearnetNamedClient = "payjoin.clearnet";
        public static readonly ScriptPubKeyType[] SupportedFormats = {
            ScriptPubKeyType.Segwit,
            ScriptPubKeyType.SegwitP2SH
        };

        public const string BIP21EndpointKey = "pj";

        private readonly ExplorerClientProvider _explorerClientProvider;
        private IHttpClientFactory _httpClientFactory;

        public PayjoinClient(ExplorerClientProvider explorerClientProvider, IHttpClientFactory httpClientFactory)
        {
            if (httpClientFactory == null) throw new ArgumentNullException(nameof(httpClientFactory));
            _explorerClientProvider =
                explorerClientProvider ?? throw new ArgumentNullException(nameof(explorerClientProvider));
            _httpClientFactory =  httpClientFactory;
        }

        public async Task<PSBT> RequestPayjoin(Uri endpoint, DerivationSchemeSettings derivationSchemeSettings,
            PSBT originalTx, CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (derivationSchemeSettings == null) throw new ArgumentNullException(nameof(derivationSchemeSettings));
            if (originalTx == null) throw new ArgumentNullException(nameof(originalTx));
            if (originalTx.IsAllFinalized())
                throw new InvalidOperationException("The original PSBT should not be finalized.");

            var type = derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();
            if (!SupportedFormats.Contains(type))
            {
                throw new PayjoinSenderException($"The wallet does not support payjoin");
            }
            var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
            var sentBefore = -originalTx.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());
            var oldGlobalTx = originalTx.GetGlobalTransaction();
            if (!originalTx.TryGetEstimatedFeeRate(out var originalFeeRate) || !originalTx.TryGetVirtualSize(out var oldVirtualSize))
                throw new ArgumentException("originalTx should have utxo information", nameof(originalTx));
            var originalFee = originalTx.GetFee();
            var cloned = originalTx.Clone();
            if (!cloned.TryFinalize(out var errors))
            {
                return null;
            }

            // We make sure we don't send unnecessary information to the receiver
            foreach (var finalized in cloned.Inputs.Where(i => i.IsFinalized()))
            {
                finalized.ClearForFinalize();
            }

            foreach (var output in cloned.Outputs)
            {
                output.HDKeyPaths.Clear();
            }

            cloned.GlobalXPubs.Clear();
            using HttpClient client = CreateHttpClient(endpoint);
            var bpuresponse = await client.PostAsync(endpoint,
                new StringContent(cloned.ToHex(), Encoding.UTF8, "text/plain"), cancellationToken);
            if (!bpuresponse.IsSuccessStatusCode)
            {
                var errorStr = await bpuresponse.Content.ReadAsStringAsync();
                try
                {
                    var error = JObject.Parse(errorStr);
                    throw new PayjoinReceiverException((int)bpuresponse.StatusCode, error["errorCode"].Value<string>(),
                        error["message"].Value<string>());
                }
                catch (JsonReaderException)
                {
                    // will throw
                    bpuresponse.EnsureSuccessStatusCode();
                    throw;
                }
            }

            var hex = await bpuresponse.Content.ReadAsStringAsync();
            var newPSBT = PSBT.Parse(hex, originalTx.Network);

            // Checking that the PSBT of the receiver is clean
            if (newPSBT.GlobalXPubs.Any())
            {
                throw new PayjoinSenderException("GlobalXPubs should not be included in the receiver's PSBT");
            }

            if (newPSBT.Outputs.Any(o => o.HDKeyPaths.Count != 0) || newPSBT.Inputs.Any(o => o.HDKeyPaths.Count != 0))
            {
                throw new PayjoinSenderException("Keypath information should not be included in the receiver's PSBT");
            }
            ////////////

            newPSBT = await _explorerClientProvider.UpdatePSBT(derivationSchemeSettings, newPSBT);
            if (newPSBT.CheckSanity() is IList<PSBTError> errors2 && errors2.Count != 0)
            {
                throw new PayjoinSenderException($"The PSBT of the receiver is insane ({errors2[0]})");
            }
            // We make sure we don't sign things what should not be signed
            foreach (var finalized in newPSBT.Inputs.Where(i => i.IsFinalized()))
            {
                finalized.ClearForFinalize();
            }
            // Make sure only the only our output have any information
            foreach (var output in newPSBT.Outputs)
            {
                output.HDKeyPaths.Clear();
                foreach (var originalOutput in  originalTx.Outputs)
                {
                    if (output.ScriptPubKey == originalOutput.ScriptPubKey)
                        output.UpdateFrom(originalOutput);
                }
            }

            // Making sure that our inputs are finalized, and that some of our inputs have not been added
            var newGlobalTx = newPSBT.GetGlobalTransaction();
            int ourInputCount = 0;
            if (newGlobalTx.Version != oldGlobalTx.Version)
                throw new PayjoinSenderException("The version field of the transaction has been modified");
            if (newGlobalTx.LockTime != oldGlobalTx.LockTime)
                throw new PayjoinSenderException("The LockTime field of the transaction has been modified");
            foreach (var input in newPSBT.Inputs.CoinsFor(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey, signingAccount.GetRootedKeyPath()))
            {
                if (oldGlobalTx.Inputs.FindIndexedInput(input.PrevOut) is IndexedTxIn ourInput)
                {
                    ourInputCount++;
                    if (input.IsFinalized())
                        throw new PayjoinSenderException("A PSBT input from us should not be finalized");
                    if (newGlobalTx.Inputs[input.Index].Sequence != ourInput.TxIn.Sequence)
                        throw new PayjoinSenderException("The sequence of one of our input has been modified");
                }
                else
                {
                    throw new PayjoinSenderException(
                        "The payjoin receiver added some of our own inputs in the proposal");
                }
            }

            foreach (var input in newPSBT.Inputs)
            {
                if (originalTx.Inputs.FindIndexedInput(input.PrevOut) is null)
                {
                    if (!input.IsFinalized())
                        throw new PayjoinSenderException("The payjoin receiver included a non finalized input");
                    // Making sure that the receiver's inputs are finalized and match format
                    var payjoinInputType = input.GetInputScriptPubKeyType();
                    if (payjoinInputType is null || payjoinInputType.Value != type)
                    {
                        throw new PayjoinSenderException("The payjoin receiver included an input that is not the same segwit input type");
                    }
                }
            }

            if (ourInputCount < originalTx.Inputs.Count)
                throw new PayjoinSenderException("The payjoin receiver removed some of our inputs");

            // We limit the number of inputs the receiver can add
            var addedInputs = newPSBT.Inputs.Count - originalTx.Inputs.Count;
            if (addedInputs == 0)
                throw new PayjoinSenderException("The payjoin receiver did not added any input");

            var sentAfter = -newPSBT.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());
            if (sentAfter > sentBefore)
            {
                var overPaying = sentAfter - sentBefore;
               
                if (!newPSBT.TryGetEstimatedFeeRate(out var newFeeRate) || !newPSBT.TryGetVirtualSize(out var newVirtualSize))
                    throw new PayjoinSenderException("The payjoin receiver did not included UTXO information to calculate fee correctly");
                
                var additionalFee = newPSBT.GetFee() - originalFee;
                if (overPaying > additionalFee)
                    throw new PayjoinSenderException("The payjoin receiver is sending more money to himself");
                if (overPaying > originalFee)
                    throw new PayjoinSenderException("The payjoin receiver is making us pay more than twice the original fee");

                // Let's check the difference is only for the fee and that feerate
                // did not changed that much
                var expectedFee = originalFeeRate.GetFee(newVirtualSize);
                // Signing precisely is hard science, give some breathing room for error.
                expectedFee += originalFeeRate.GetFee(newPSBT.Inputs.Count * 2);
                if (overPaying > (expectedFee - originalFee))
                    throw new PayjoinSenderException("The payjoin receiver increased the fee rate we are paying too much");
            }

            return newPSBT;
        }

        private HttpClient CreateHttpClient(Uri uri)
        {
            if (uri.IsOnion())
                return _httpClientFactory.CreateClient(PayjoinOnionNamedClient);
            else
                return _httpClientFactory.CreateClient(PayjoinClearnetNamedClient);
        }
    }

    public class PayjoinException : Exception
    {
        public PayjoinException(string message) : base(message)
        {
        }
    }

    public class PayjoinReceiverException : PayjoinException
    {
        public PayjoinReceiverException(int httpCode, string errorCode, string message) : base(FormatMessage(httpCode,
            errorCode, message))
        {
            HttpCode = httpCode;
            ErrorCode = errorCode;
            ErrorMessage = message;
        }

        public int HttpCode { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private static string FormatMessage(in int httpCode, string errorCode, string message)
        {
            return $"{errorCode}: {message} (HTTP: {httpCode})";
        }
    }

    public class PayjoinSenderException : PayjoinException
    {
        public PayjoinSenderException(string message) : base(message)
        {
        }
    }
}
