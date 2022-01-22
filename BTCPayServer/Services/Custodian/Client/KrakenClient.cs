using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Custodian.Client.Exception;
using Microsoft.AspNetCore.WebUtilities;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian.Client;

public class KrakenClient : ICustodian, ICanDeposit
{
    private readonly HttpClient _client;

    public KrakenClient(IHttpClientFactory httpClientFactory)
    {
        _client = httpClientFactory.CreateClient();
    }

    public string getCode()
    {
        return "kraken";
    }

    public string getName()
    {
        return "Kraken";
    }

    public string[] getSupportedAssets()
    {
        // TODO use API to get a full list.
        return new string[] { "BTC", "LTC" };
    }

    public string[]? getTradableAssetPairs()
    {
        // TODO use API to get a full list.
        return new string[] { "XBTEUR", "XBTUSD", "LTCEUR", "LTCUSD" };
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalances(CustodianAccountResponse custodianAccountResponse)
    {
        // TODO the use of "CustodianAccountResponse" is sloppy. We should prolly use the Model or Data class
        var apiKey = custodianAccountResponse.Config["apiKey"].ToString();
        var privateKey = custodianAccountResponse.Config["privateKey"].ToString();

        var data = await QueryPrivate("Balance", null, apiKey, privateKey, new CancellationToken());
        var balances = data["result"];
        if (balances is JObject)
        {
            var r = new Dictionary<string, decimal>();
            var balancesJObject = (JObject)balances;
            foreach (var keyValuePair in balancesJObject)
            {
                if (keyValuePair.Value != null)
                {
                    decimal amount = Convert.ToDecimal(keyValuePair.Value.ToString(), CultureInfo.InvariantCulture);
                    r.Add(keyValuePair.Key, amount);
                }
            }
            return r;
        }
        return null;
    }

    public DepositAddressData GetDepositAddress(string paymentMethod)
    {
        if (paymentMethod == "BTC-OnChain")
        {
            // TODO use API to get this address.
            var result = new DepositAddressData();
            result.Address = "";
            result.Type = "";

            return result;
        }

        throw new NotImplementedException("Only BTC-OnChain is implemented right now.");
    }

    private async Task<JObject> QueryPrivate(string method, Dictionary<string, string>? param, string apiKey,
        string privateKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string nonce = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "000";

        var postData = new Dictionary<string, string>();
        if (param != null)
        {
            foreach (KeyValuePair<string, string> keyValuePair in param)
            {
                postData.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }

        postData.Add("nonce", nonce);

        var postDataString = QueryHelpers.AddQueryString("", postData).Remove(0, 1);
        var path = "/0/private/" + method;
        var url = "https://api.kraken.com" + path;
        var decodedSecret = Convert.FromBase64String(privateKey);

        var sha256 = SHA256.Create();
        var hmac512 = new System.Security.Cryptography.HMACSHA512(decodedSecret);

        var unhashed1 = nonce.ToString(CultureInfo.InvariantCulture) + postDataString;
        var hash1 = sha256.ComputeHash(Encoding.UTF8.GetBytes(unhashed1));
        var pathBytes = Encoding.UTF8.GetBytes(path);

        byte[] unhashed2 = new byte[path.Length + hash1.Length];
        System.Buffer.BlockCopy(pathBytes, 0, unhashed2, 0, pathBytes.Length);
        System.Buffer.BlockCopy(hash1, 0, unhashed2, pathBytes.Length, hash1.Length);

        var signature = hmac512.ComputeHash(unhashed2);
        var apiSign = Convert.ToBase64String(signature);

        HttpRequestMessage request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        request.Headers.Add("API-Key", apiKey);
        request.Headers.Add("API-Sign", apiSign);
        request.Headers.Add("User-Agent", $"BTCPayServer/{GetVersion()}");
        request.RequestUri = new Uri(url, UriKind.Absolute);
        request.Content =
            new StringContent(postDataString, new UTF8Encoding(false), "application/x-www-form-urlencoded");

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(0.5));

        var response = await _client.SendAsync(request, cts.Token);
        var responseString = response.Content.ReadAsStringAsync().Result;
        var r = JObject.Parse(responseString);

        var errorMessage = r["error"];
        if (errorMessage is JArray)
        {
            var errorMessageArray = ((JArray)errorMessage);
            if (errorMessageArray.Count > 0)
            {
                throw new KrakenApiException(errorMessageArray[0].ToString());
            }
        }

        return r;
    }

    private string GetVersion()
    {
        // TODO this was a copy-paste of somewhere else. Should be put this method somewhere accessible to all?
        return typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()
            .Version;
    }
}
