using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Util;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class PayjoinClient
    {
        private readonly ExplorerClientProvider _explorerClientProvider;
        private HttpClient _httpClient;

        public PayjoinClient(ExplorerClientProvider explorerClientProvider, IHttpClientFactory httpClientFactory)
        {
            if (httpClientFactory == null) throw new ArgumentNullException(nameof(httpClientFactory));
            _explorerClientProvider =
                explorerClientProvider ?? throw new ArgumentNullException(nameof(explorerClientProvider));
            _httpClient = httpClientFactory.CreateClient("payjoin");
        }

        public async Task<PSBT> RequestPayjoin(Uri endpoint, DerivationSchemeSettings derivationSchemeSettings,
            PSBT originalTx, CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (derivationSchemeSettings == null) throw new ArgumentNullException(nameof(derivationSchemeSettings));
            if (originalTx == null) throw new ArgumentNullException(nameof(originalTx));

            var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
            var sentBefore = -originalTx.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());

            if (!originalTx.TryGetEstimatedFeeRate(out var oldFeeRate))
                throw new ArgumentException("originalTx should have utxo information", nameof(originalTx));
            var cloned = originalTx.Clone();
            if (!cloned.IsAllFinalized() && !cloned.TryFinalize(out var errors))
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
            var bpuresponse = await _httpClient.PostAsync(endpoint,
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
            int ourInputCount = 0;
            foreach (var input in newPSBT.Inputs.CoinsFor(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey, signingAccount.GetRootedKeyPath()))
            {
                if (originalTx.Inputs.FindIndexedInput(input.PrevOut) is PSBTInput ourInput)
                {
                    ourInputCount++;
                    if (input.IsFinalized())
                        throw new PayjoinSenderException("A PSBT input from us should not be finalized");
                }
                else
                {
                    throw new PayjoinSenderException(
                        "The payjoin receiver added some of our own inputs in the proposal");
                }
            }

            // Making sure that the receiver's inputs are finalized
            foreach (var input in newPSBT.Inputs)
            {
                if (originalTx.Inputs.FindIndexedInput(input.PrevOut) is null && !input.IsFinalized())
                    throw new PayjoinSenderException("The payjoin receiver included a non finalized input");
            }

            if (ourInputCount < originalTx.Inputs.Count)
                throw new PayjoinSenderException("The payjoin receiver removed some of our inputs");

            // We limit the number of inputs the receiver can add
            var addedInputs = newPSBT.Inputs.Count - originalTx.Inputs.Count;
            if (originalTx.Inputs.Count < addedInputs)
                throw new PayjoinSenderException("The payjoin receiver added too much inputs");

            var sentAfter = -newPSBT.GetBalance(derivationSchemeSettings.AccountDerivation,
                signingAccount.AccountKey,
                signingAccount.GetRootedKeyPath());
            if (sentAfter > sentBefore)
            {
                if (!newPSBT.TryGetEstimatedFeeRate(out var newFeeRate) || !newPSBT.TryGetVirtualSize(out var newVirtualSize))
                    throw new PayjoinSenderException("The payjoin receiver did not included UTXO information to calculate fee correctly");
                // Let's check the difference is only for the fee and that feerate
                // did not changed that much
                var expectedFee = oldFeeRate.GetFee(newVirtualSize);
                // Signing precisely is hard science, give some breathing room for error.
                expectedFee += newPSBT.Inputs.Count * Money.Satoshis(2);
                
                // If the payjoin is removing some dust, we may pay a bit more as a whole output has been removed
                var removedOutputs = Math.Max(0, originalTx.Outputs.Count - newPSBT.Outputs.Count);
                expectedFee +=  removedOutputs * oldFeeRate.GetFee(294);

                var actualFee = newFeeRate.GetFee(newVirtualSize);
                if (actualFee > expectedFee && actualFee - expectedFee > Money.Satoshis(546))
                    throw new PayjoinSenderException("The payjoin receiver is paying too much fee");
            }

            return newPSBT;
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
