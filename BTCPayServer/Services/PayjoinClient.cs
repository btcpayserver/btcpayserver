using System;
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
            _explorerClientProvider = explorerClientProvider ?? throw new ArgumentNullException(nameof(explorerClientProvider));
            _httpClient = httpClientFactory.CreateClient("payjoin");
        }

        public async Task<PSBT> RequestPayjoin(Uri endpoint, DerivationSchemeSettings derivationSchemeSettings, PSBT originalTx, CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (derivationSchemeSettings == null) throw new ArgumentNullException(nameof(derivationSchemeSettings));
            if (originalTx == null) throw new ArgumentNullException(nameof(originalTx));
            var cloned = originalTx.Clone();
            if (!cloned.IsAllFinalized() && !cloned.TryFinalize(out var errors))
            {
                return null;
            }
            foreach (var output in cloned.Outputs)
                output.HDKeyPaths.Clear();
            cloned.GlobalXPubs.Clear();
            var bpuresponse = await _httpClient.PostAsync(endpoint,
                new StringContent(cloned.ToHex(), Encoding.UTF8, "text/plain"), cancellationToken);
            if (bpuresponse.IsSuccessStatusCode)
            {
                var hex = await bpuresponse.Content.ReadAsStringAsync();
                if (PSBT.TryParse(hex, originalTx.Network, out var newPSBT))
                {
                    //check that all the inputs we provided are still there and that there is at least one new(signed) input.
                    bool valid = false;
                    var existingInputs = originalTx.Inputs.Select(input => input.PrevOut).ToHashSet();
                    foreach (var input in newPSBT.Inputs)
                    {
                        var existingInput = existingInputs.SingleOrDefault(point => point == input.PrevOut);
                        if (existingInput != null)
                        {
                            existingInputs.Remove(existingInput);
                            continue;
                        }

                        if (!input.TryFinalizeInput(out _))
                        {
                            //a new input was provided but was invalid.
                            valid = false;
                            break;
                        }

                        // a new signed input was provided
                        valid = true;
                    }

                    if (!valid || existingInputs.Any())
                    {
                        return null;
                    }

                    newPSBT = await _explorerClientProvider.UpdatePSBT(derivationSchemeSettings, newPSBT);
                    //check if output sum to self is the same.
                    var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
                    var balanceAfter = newPSBT.GetBalance(derivationSchemeSettings.AccountDerivation,
                        signingAccount.AccountKey,
                        signingAccount.GetRootedKeyPath());
                    var balanceBefore = originalTx.GetBalance(derivationSchemeSettings.AccountDerivation,
                        signingAccount.AccountKey,
                        signingAccount.GetRootedKeyPath());
                    if (balanceAfter is null)
                        return null;
                    if (balanceAfter < balanceBefore)
                    {
                        // TODO: Validate balance
                        // // Let's check the difference is only for the fee and that feerate
                        // // did not changed that much
                        // var additionalFee = balanceAfter - balanceBefore;
                        // if (!newPSBT.TryGetFee(out var newFee))
                        //     return null;
                        // if (!originalTx.TryGetFee(out var oldFee))
                        //     return null;
                        // if (additionalFee != newFee - oldFee)
                        //     return null;
                        // // Let's check the feerate did not changed
                    }

                    return newPSBT;
                }
            }
            else
            {
                var errorStr = await bpuresponse.Content.ReadAsStringAsync();
                try
                {
                    var error = JObject.Parse(errorStr);
                    throw new PayjoinException((int)bpuresponse.StatusCode, error["errorCode"].Value<string>(), error["message"].Value<string>());
                }
                catch (JsonReaderException)
                {
                    // will throw
                    bpuresponse.EnsureSuccessStatusCode();
                    throw;
                }
            }
            return null;
        }
    }

    public class PayjoinException : Exception
    {
        public PayjoinException(int httpCode, string errorCode, string message): base(FormatMessage(httpCode, errorCode, message))
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
}
