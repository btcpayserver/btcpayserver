using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
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
                PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(i.FinalScriptWitness) is { })
                return ScriptPubKeyType.SegwitP2SH;
            return null;
        }
    }

    public class PayjoinClientParameters
    {
        public Money MaxAdditionalFeeContribution { get; set; }
        public FeeRate MinFeeRate { get; set; }
        public int? AdditionalFeeOutputIndex { get; set; }
        public int Version { get; set; } = 1;
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
            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));
            _explorerClientProvider =
                explorerClientProvider ?? throw new ArgumentNullException(nameof(explorerClientProvider));
            _httpClientFactory = httpClientFactory;
        }

        public Money MaxFeeBumpContribution { get; set; }
        public FeeRate MinimumFeeRate { get; set; }

        public async Task<PSBT> RequestPayjoin(Uri endpoint, DerivationSchemeSettings derivationSchemeSettings,
            PSBT originalTx, CancellationToken cancellationToken)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (derivationSchemeSettings == null)
                throw new ArgumentNullException(nameof(derivationSchemeSettings));
            if (originalTx == null)
                throw new ArgumentNullException(nameof(originalTx));
            if (originalTx.IsAllFinalized())
                throw new InvalidOperationException("The original PSBT should not be finalized.");
            var clientParameters = new PayjoinClientParameters();
            var type = derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();
            if (!SupportedFormats.Contains(type))
            {
                throw new PayjoinSenderException($"The wallet does not support payjoin");
            }
            var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
            var changeOutput = originalTx.Outputs.CoinsFor(derivationSchemeSettings.AccountDerivation, signingAccount.AccountKey, signingAccount.GetRootedKeyPath())
                    .FirstOrDefault();
            if (changeOutput is PSBTOutput o)
                clientParameters.AdditionalFeeOutputIndex = (int)o.Index;
            var sentBefore = -originalTx.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());
            var oldGlobalTx = originalTx.GetGlobalTransaction();
            if (!originalTx.TryGetEstimatedFeeRate(out var originalFeeRate) || !originalTx.TryGetVirtualSize(out var oldVirtualSize))
                throw new ArgumentException("originalTx should have utxo information", nameof(originalTx));
            var originalFee = originalTx.GetFee();
            clientParameters.MaxAdditionalFeeContribution = MaxFeeBumpContribution is null ? originalFee : MaxFeeBumpContribution;
            if (MinimumFeeRate is FeeRate v)
                clientParameters.MinFeeRate = v;
            var cloned = originalTx.Clone();
            cloned.Finalize();

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

            endpoint = ApplyOptionalParameters(endpoint, clientParameters);
            using HttpClient client = CreateHttpClient(endpoint);
            var bpuresponse = await client.PostAsync(endpoint,
                new StringContent(cloned.ToHex(), Encoding.UTF8, "text/plain"), cancellationToken);
            if (!bpuresponse.IsSuccessStatusCode)
            {
                var errorStr = await bpuresponse.Content.ReadAsStringAsync();
                try
                {
                    var error = JObject.Parse(errorStr);
                    throw new PayjoinReceiverException(error["errorCode"].Value<string>(),
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
                foreach (var originalOutput in originalTx.Outputs)
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

            if (!newPSBT.TryGetEstimatedFeeRate(out var newFeeRate) || !newPSBT.TryGetVirtualSize(out var newVirtualSize))
                throw new PayjoinSenderException("The payjoin receiver did not included UTXO information to calculate fee correctly");

            if (clientParameters.MinFeeRate is FeeRate minFeeRate)
            {
                if (newFeeRate < minFeeRate)
                    throw new PayjoinSenderException("The payjoin receiver created a payjoin with a too low fee rate");
            }

            var sentAfter = -newPSBT.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());
            if (sentAfter > sentBefore)
            {
                var overPaying = sentAfter - sentBefore;
                var additionalFee = newPSBT.GetFee() - originalFee;
                if (overPaying > additionalFee)
                    throw new PayjoinSenderException("The payjoin receiver is sending more money to himself");
                if (overPaying > clientParameters.MaxAdditionalFeeContribution)
                    throw new PayjoinSenderException("The payjoin receiver is making us pay too much fee");

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

        private static Uri ApplyOptionalParameters(Uri endpoint, PayjoinClientParameters clientParameters)
        {
            var requestUri = endpoint.AbsoluteUri;
            if (requestUri.IndexOf('?', StringComparison.OrdinalIgnoreCase) is int i && i != -1)
                requestUri = requestUri.Substring(0, i);
            List<string> parameters = new List<string>(3);
            parameters.Add($"v={clientParameters.Version}");
            if (clientParameters.AdditionalFeeOutputIndex is int additionalFeeOutputIndex)
                parameters.Add($"additionalfeeoutputindex={additionalFeeOutputIndex.ToString(CultureInfo.InvariantCulture)}");
            if (clientParameters.MaxAdditionalFeeContribution is Money maxAdditionalFeeContribution)
                parameters.Add($"maxadditionalfeecontribution={maxAdditionalFeeContribution.Satoshi.ToString(CultureInfo.InvariantCulture)}");
            if (clientParameters.MinFeeRate is FeeRate minFeeRate)
                parameters.Add($"minfeerate={minFeeRate.SatoshiPerByte.ToString(CultureInfo.InvariantCulture)}");
            endpoint = new Uri($"{requestUri}?{string.Join('&', parameters)}");
            return endpoint;
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

    public enum PayjoinReceiverWellknownErrors
    {
        LeakingData,
        PSBTNotFinalized,
        Unavailable,
        OutOfUTXOS,
        NotEnoughMoney,
        InsanePSBT,
        VersionUnsupported,
        NeedUTXOInformation,
        InvalidTransaction
    }
    public class PayjoinReceiverException : PayjoinException
    {
        public PayjoinReceiverException(string errorCode, string receiverDebugMessage) : base(FormatMessage(errorCode, receiverDebugMessage))
        {
            ErrorCode = errorCode;
            ReceiverDebugMessage = receiverDebugMessage;
            WellknownError = errorCode switch
            {
                "leaking-data" => PayjoinReceiverWellknownErrors.LeakingData,
                "psbt-not-finalized" => PayjoinReceiverWellknownErrors.PSBTNotFinalized,
                "unavailable" => PayjoinReceiverWellknownErrors.Unavailable,
                "out-of-utxos" => PayjoinReceiverWellknownErrors.OutOfUTXOS,
                "not-enough-money" => PayjoinReceiverWellknownErrors.NotEnoughMoney,
                "insane-psbt" => PayjoinReceiverWellknownErrors.InsanePSBT,
                "version-unsupported" => PayjoinReceiverWellknownErrors.VersionUnsupported,
                "need-utxo-information" => PayjoinReceiverWellknownErrors.NeedUTXOInformation,
                "invalid-transaction" => PayjoinReceiverWellknownErrors.InvalidTransaction,
                _ => null
            };
        }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public string ReceiverDebugMessage { get; }

        public PayjoinReceiverWellknownErrors? WellknownError
        {
            get;
        }

        private static string FormatMessage(string errorCode, string receiverDebugMessage)
        {
            return $"{errorCode}: {GetMessage(errorCode)}";
        }

        private static string GetMessage(string errorCode)
        {
            return errorCode switch
            {
                "leaking-data" => "Key path information or GlobalXPubs should not be included in the original PSBT.",
                "psbt-not-finalized" => "The original PSBT must be finalized.",
                "unavailable" => "The payjoin endpoint is not available for now.",
                "out-of-utxos" => "The receiver does not have any UTXO to contribute in a payjoin proposal.",
                "not-enough-money" => "The receiver added some inputs but could not bump the fee of the payjoin proposal.",
                "insane-psbt" => "Some consistency check on the PSBT failed.",
                "version-unsupported" => "This version of payjoin is not supported.",
                "need-utxo-information" => "The witness UTXO or non witness UTXO is missing",
                "invalid-transaction" => "The original transaction is invalid for payjoin",
                _ => "Unknown error"
            };
        }
    }

    public class PayjoinSenderException : PayjoinException
    {
        public PayjoinSenderException(string message) : base(message)
        {
        }
    }
}
