using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Payment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IHttpClientFactory = System.Net.Http.IHttpClientFactory;

namespace BTCPayServer.Services
{

    public static class PSBTExtensions
    {
        public static ScriptPubKeyType? GetInputsScriptPubKeyType(this PSBT psbt)
        {
            if (!psbt.IsAllFinalized())
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
            var scriptPubKey = i.GetTxOut().ScriptPubKey;
            if (scriptPubKey.IsScriptType(ScriptType.P2PKH))
                return ScriptPubKeyType.Legacy;
            if (scriptPubKey.IsScriptType(ScriptType.P2WPKH))
                return ScriptPubKeyType.Segwit;
            if (scriptPubKey.IsScriptType(ScriptType.P2SH) &&
                i.FinalScriptWitness is WitScript &&
                PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(i.FinalScriptWitness) is { })
                return ScriptPubKeyType.SegwitP2SH;
            if (scriptPubKey.IsScriptType(ScriptType.P2SH) &&
                i.RedeemScript is Script &&
                PayToWitPubKeyHashTemplate.Instance.CheckScriptPubKey(i.RedeemScript))
                return ScriptPubKeyType.SegwitP2SH;
            return null;
        }
    }

    public class PayjoinClientParameters
    {
        public Money MaxAdditionalFeeContribution { get; set; }
        public FeeRate MinFeeRate { get; set; }
        public int? AdditionalFeeOutputIndex { get; set; }
        public bool? DisableOutputSubstitution { get; set; }
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
        private readonly IHttpClientFactory _httpClientFactory;

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

        public async Task<PSBT> RequestPayjoin(BitcoinUrlBuilder bip21, DerivationSchemeSettings derivationSchemeSettings,
            PSBT signedPSBT, CancellationToken cancellationToken)
        {
            if (bip21 == null)
                throw new ArgumentNullException(nameof(bip21));
            if (!bip21.TryGetPayjoinEndpoint(out var endpoint))
                throw new InvalidOperationException("This BIP21 does not support payjoin");
            if (derivationSchemeSettings == null)
                throw new ArgumentNullException(nameof(derivationSchemeSettings));
            if (signedPSBT == null)
                throw new ArgumentNullException(nameof(signedPSBT));
            if (signedPSBT.IsAllFinalized())
                throw new InvalidOperationException("The original PSBT should not be finalized.");
            var optionalParameters = new PayjoinClientParameters();
            var inputScriptType = derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();
            var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
            var paymentScriptPubKey = bip21.Address?.ScriptPubKey;
            var changeOutput = signedPSBT.Outputs.CoinsFor(derivationSchemeSettings.AccountDerivation, signingAccount.AccountKey, signingAccount.GetRootedKeyPath())
                    .Where(o => o.ScriptPubKey != paymentScriptPubKey)
                    .FirstOrDefault();
            if (changeOutput is PSBTOutput o)
                optionalParameters.AdditionalFeeOutputIndex = (int)o.Index;
            if (!signedPSBT.TryGetEstimatedFeeRate(out var originalFeeRate))
                throw new ArgumentException("signedPSBT should have utxo information", nameof(signedPSBT));
            var originalFee = signedPSBT.GetFee();
            optionalParameters.MaxAdditionalFeeContribution = MaxFeeBumpContribution is null ?
                // By default, we want to keep same fee rate and a single additional input
                originalFeeRate.GetFee(GetVirtualSize(inputScriptType)) :
                MaxFeeBumpContribution;
            if (MinimumFeeRate is FeeRate v)
                optionalParameters.MinFeeRate = v;

            bool allowOutputSubstitution = !(optionalParameters.DisableOutputSubstitution is true);
            if (bip21.UnknowParameters.TryGetValue("pjos", out var pjos) && pjos == "0")
                allowOutputSubstitution = false;
            PSBT originalPSBT = CreateOriginalPSBT(signedPSBT);
            Transaction originalGlobalTx = signedPSBT.GetGlobalTransaction();
            TxOut feeOutput = changeOutput == null ? null : originalGlobalTx.Outputs[changeOutput.Index];
            var originalInputs = new Queue<(TxIn OriginalTxIn, PSBTInput SignedPSBTInput)>();
            for (int i = 0; i < originalGlobalTx.Inputs.Count; i++)
            {
                originalInputs.Enqueue((originalGlobalTx.Inputs[i], signedPSBT.Inputs[i]));
            }
            var originalOutputs = new Queue<(TxOut OriginalTxOut, PSBTOutput SignedPSBTOutput)>();
            for (int i = 0; i < originalGlobalTx.Outputs.Count; i++)
            {
                originalOutputs.Enqueue((originalGlobalTx.Outputs[i], signedPSBT.Outputs[i]));
            }
            endpoint = ApplyOptionalParameters(endpoint, optionalParameters);
            var proposal = await SendOriginalTransaction(endpoint, originalPSBT, cancellationToken);
            // Checking that the PSBT of the receiver is clean
            if (proposal.GlobalXPubs.Any())
            {
                throw new PayjoinSenderException("GlobalXPubs should not be included in the receiver's PSBT");
            }
            ////////////

            if (proposal.CheckSanity() is List<PSBTError> errors && errors.Count > 0)
                throw new PayjoinSenderException($"The proposal PSBT is not sane ({errors[0]})");

            var proposalGlobalTx = proposal.GetGlobalTransaction();
            // Verify that the transaction version, and nLockTime are unchanged.
            if (proposalGlobalTx.Version != originalGlobalTx.Version)
                throw new PayjoinSenderException($"The proposal PSBT changed the transaction version");
            if (proposalGlobalTx.LockTime != originalGlobalTx.LockTime)
                throw new PayjoinSenderException($"The proposal PSBT changed the nLocktime");

            HashSet<Sequence> sequences = new HashSet<Sequence>();
            // For each inputs in the proposal:
            foreach (var proposedPSBTInput in proposal.Inputs)
            {
                if (proposedPSBTInput.HDKeyPaths.Count != 0)
                    throw new PayjoinSenderException("The receiver added keypaths to an input");
                if (proposedPSBTInput.PartialSigs.Count != 0)
                    throw new PayjoinSenderException("The receiver added partial signatures to an input");
                var proposedTxIn = proposalGlobalTx.Inputs.FindIndexedInput(proposedPSBTInput.PrevOut).TxIn;
                bool isOurInput = originalInputs.Count > 0 && originalInputs.Peek().OriginalTxIn.PrevOut == proposedPSBTInput.PrevOut;
                // If it is one of our input
                if (isOurInput)
                {
                    var input = originalInputs.Dequeue();
                    // Verify that sequence is unchanged.
                    if (input.OriginalTxIn.Sequence != proposedTxIn.Sequence)
                        throw new PayjoinSenderException("The proposedTxIn modified the sequence of one of our inputs");
                    // Verify the PSBT input is not finalized
                    if (proposedPSBTInput.IsFinalized())
                        throw new PayjoinSenderException("The receiver finalized one of our inputs");
                    // Verify that <code>non_witness_utxo</code> and <code>witness_utxo</code> are not specified.
                    if (proposedPSBTInput.NonWitnessUtxo != null || proposedPSBTInput.WitnessUtxo != null)
                        throw new PayjoinSenderException("The receiver added non_witness_utxo or witness_utxo to one of our inputs");
                    sequences.Add(proposedTxIn.Sequence);

                    // Fill up the info from the original PSBT input so we can sign and get fees.
                    proposedPSBTInput.NonWitnessUtxo = input.SignedPSBTInput.NonWitnessUtxo;
                    proposedPSBTInput.WitnessUtxo = input.SignedPSBTInput.WitnessUtxo;
                    // We fill up information we had on the signed PSBT, so we can sign it.
                    foreach (var hdKey in input.SignedPSBTInput.HDKeyPaths)
                        proposedPSBTInput.HDKeyPaths.Add(hdKey.Key, hdKey.Value);
                    proposedPSBTInput.RedeemScript = input.SignedPSBTInput.RedeemScript;
                }
                else
                {
                    // Verify the PSBT input is finalized
                    if (!proposedPSBTInput.IsFinalized())
                        throw new PayjoinSenderException("The receiver did not finalized one of their input");
                    // Verify that non_witness_utxo or witness_utxo are filled in.
                    if (proposedPSBTInput.NonWitnessUtxo == null && proposedPSBTInput.WitnessUtxo == null)
                        throw new PayjoinSenderException("The receiver did not specify non_witness_utxo or witness_utxo for one of their inputs");
                    sequences.Add(proposedTxIn.Sequence);
                    // Verify that the payjoin proposal did not introduced mixed input's type.
                    if (inputScriptType != proposedPSBTInput.GetInputScriptPubKeyType())
                        throw new PayjoinSenderException("Mixed input type detected in the proposal");
                }
            }

            // Verify that all of sender's inputs from the original PSBT are in the proposal.
            if (originalInputs.Count != 0)
                throw new PayjoinSenderException("Some of our inputs are not included in the proposal");

            // Verify that the payjoin proposal did not introduced mixed input's sequence.
            if (sequences.Count != 1)
                throw new PayjoinSenderException("Mixed sequence detected in the proposal");

            if (!proposal.TryGetFee(out var newFee))
                throw new PayjoinSenderException("The payjoin receiver did not included UTXO information to calculate fee correctly");
            var additionalFee = newFee - originalFee;
            if (additionalFee < Money.Zero)
                throw new PayjoinSenderException("The receiver decreased absolute fee");

            // For each outputs in the proposal:
            foreach (var proposedPSBTOutput in proposal.Outputs)
            {
                // Verify that no keypaths is in the PSBT output
                if (proposedPSBTOutput.HDKeyPaths.Count != 0)
                    throw new PayjoinSenderException("The receiver added keypaths to an output");
                bool isOriginalOutput = originalOutputs.Count > 0 && originalOutputs.Peek().OriginalTxOut.ScriptPubKey == proposedPSBTOutput.ScriptPubKey;
                if (isOriginalOutput)
                {
                    var originalOutput = originalOutputs.Dequeue();
                    if (originalOutput.OriginalTxOut == feeOutput)
                    {
                        var actualContribution = feeOutput.Value - proposedPSBTOutput.Value;
                        // The amount that was substracted from the output's value is less or equal to maxadditionalfeecontribution
                        if (actualContribution > optionalParameters.MaxAdditionalFeeContribution)
                            throw new PayjoinSenderException("The actual contribution is more than maxadditionalfeecontribution");
                        // Make sure the actual contribution is only paying fee
                        if (actualContribution > additionalFee)
                            throw new PayjoinSenderException("The actual contribution is not only paying fee");
                        // Make sure the actual contribution is only paying for fee incurred by additional inputs
                        var additionalInputsCount = proposalGlobalTx.Inputs.Count - originalGlobalTx.Inputs.Count;
                        if (actualContribution > originalFeeRate.GetFee(GetVirtualSize(inputScriptType)) * additionalInputsCount)
                            throw new PayjoinSenderException("The actual contribution is not only paying for additional inputs");
                    }
                    else if (allowOutputSubstitution &&
                        originalOutput.OriginalTxOut.ScriptPubKey == paymentScriptPubKey)
                    {
                        // That's the payment output, the receiver may have changed it.
                    }
                    else
                    {
                        if (originalOutput.OriginalTxOut.Value > proposedPSBTOutput.Value)
                            throw new PayjoinSenderException("The receiver decreased the value of one of the outputs");
                    }
                    // We fill up information we had on the signed PSBT, so we can sign it.
                    foreach (var hdKey in originalOutput.SignedPSBTOutput.HDKeyPaths)
                        proposedPSBTOutput.HDKeyPaths.Add(hdKey.Key, hdKey.Value);
                    proposedPSBTOutput.RedeemScript = originalOutput.SignedPSBTOutput.RedeemScript;
                }
            }
            // Verify that all of sender's outputs from the original PSBT are in the proposal.
            if (originalOutputs.Count != 0)
            {
                if (!allowOutputSubstitution ||
                    originalOutputs.Count != 1 ||
                    originalOutputs.Dequeue().OriginalTxOut.ScriptPubKey != paymentScriptPubKey)
                {
                    throw new PayjoinSenderException("Some of our outputs are not included in the proposal");
                }
            }

            // If minfeerate was specified, check that the fee rate of the payjoin transaction is not less than this value.
            if (optionalParameters.MinFeeRate is FeeRate minFeeRate)
            {
                if (!proposal.TryGetEstimatedFeeRate(out var newFeeRate))
                    throw new PayjoinSenderException("The payjoin receiver did not included UTXO information to calculate fee correctly");
                if (newFeeRate < minFeeRate)
                    throw new PayjoinSenderException("The payjoin receiver created a payjoin with a too low fee rate");
            }
            return proposal;
        }

        private int GetVirtualSize(ScriptPubKeyType? scriptPubKeyType)
        {
            switch (scriptPubKeyType)
            {
                case ScriptPubKeyType.Legacy:
                    return 148;
                case ScriptPubKeyType.Segwit:
                    return 68;
                case ScriptPubKeyType.SegwitP2SH:
                    return 91;
                default:
                    return 110;
            }
        }

        private static PSBT CreateOriginalPSBT(PSBT signedPSBT)
        {
            var original = signedPSBT.Clone();
            original = original.Finalize();
            foreach (var input in original.Inputs)
            {
                input.HDKeyPaths.Clear();
                input.PartialSigs.Clear();
                input.Unknown.Clear();
            }
            foreach (var output in original.Outputs)
            {
                output.Unknown.Clear();
                output.HDKeyPaths.Clear();
            }
            original.GlobalXPubs.Clear();
            return original;
        }

        private async Task<PSBT> SendOriginalTransaction(Uri endpoint, PSBT originalTx, CancellationToken cancellationToken)
        {
            using (HttpClient client = CreateHttpClient(endpoint))
            {
                var bpuresponse = await client.PostAsync(endpoint,
                    new StringContent(originalTx.ToHex(), Encoding.UTF8, "text/plain"), cancellationToken);
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
                return PSBT.Parse(hex, originalTx.Network);
            }
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
            if (clientParameters.DisableOutputSubstitution is bool disableoutputsubstitution)
                parameters.Add($"disableoutputsubstitution={disableoutputsubstitution}");
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
        Unavailable,
        NotEnoughMoney,
        VersionUnsupported,
        OriginalPSBTRejected
    }
    public class PayjoinReceiverHelper
    {
        static IEnumerable<(PayjoinReceiverWellknownErrors EnumValue, string ErrorCode, string Message)> Get()
        {
            yield return (PayjoinReceiverWellknownErrors.Unavailable, "unavailable", "The payjoin endpoint is not available for now.");
            yield return (PayjoinReceiverWellknownErrors.NotEnoughMoney, "not-enough-money", "The receiver added some inputs but could not bump the fee of the payjoin proposal.");
            yield return (PayjoinReceiverWellknownErrors.VersionUnsupported, "version-unsupported", "This version of payjoin is not supported.");
            yield return (PayjoinReceiverWellknownErrors.OriginalPSBTRejected, "original-psbt-rejected", "The receiver rejected the original PSBT.");
        }
        public static string GetErrorCode(PayjoinReceiverWellknownErrors err)
        {
            return Get().Single(o => o.EnumValue == err).ErrorCode;
        }
        public static PayjoinReceiverWellknownErrors? GetWellknownError(string errorCode)
        {
            var t = Get().FirstOrDefault(o => o.ErrorCode == errorCode);
            if (t == default)
                return null;
            return t.EnumValue;
        }
        static readonly string UnknownError = "Unknown error from the receiver";
        public static string GetMessage(string errorCode)
        {
            return Get().FirstOrDefault(o => o.ErrorCode == errorCode).Message ?? UnknownError;
        }
        public static string GetMessage(PayjoinReceiverWellknownErrors err)
        {
            return Get().Single(o => o.EnumValue == err).Message;
        }
    }
    public class PayjoinReceiverException : PayjoinException
    {
        public PayjoinReceiverException(string errorCode, string receiverMessage) : base(FormatMessage(errorCode, receiverMessage))
        {
            ErrorCode = errorCode;
            ReceiverMessage = receiverMessage;
            WellknownError = PayjoinReceiverHelper.GetWellknownError(errorCode);
            ErrorMessage = PayjoinReceiverHelper.GetMessage(errorCode);
        }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public string ReceiverMessage { get; }

        public PayjoinReceiverWellknownErrors? WellknownError
        {
            get;
        }

        private static string FormatMessage(string errorCode, string receiverMessage)
        {
            return $"{errorCode}: {PayjoinReceiverHelper.GetMessage(errorCode)}. (Receiver message: {receiverMessage})";
        }
    }

    public class PayjoinSenderException : PayjoinException
    {
        public PayjoinSenderException(string message) : base(message)
        {
        }
    }
}
