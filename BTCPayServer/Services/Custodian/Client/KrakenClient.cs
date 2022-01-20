using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Http;
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

    public Dictionary<string, decimal> GetAssetBalances(CustodianAccountResponse custodianAccountResponse)
    {
        // TODO the use of "CustodianAccountResponse" is sloppy. We should prolly use the Model or Data class
        // TODO Test this...
        var apiKey = custodianAccountResponse.Config["apiKey"].ToString();
        var privateKey = custodianAccountResponse.Config["privateKey"].ToString();

        var data = QueryPrivate("Balance", null, apiKey, privateKey, new CancellationToken());

        var r = new Dictionary<string, decimal>();
        return r;

        //  $result = $this->_api->QueryPrivate('Balance');
        // $r = [];
        //
        // if (isset($result['result'])) {
        //
        //     $coins = $result['result'];
        //     foreach ($coins as $key => $value) {
        //
        //         if (bccomp($value, 0) > 0 || $key == 'ZEUR') {
        //             $r[$key] = $value;
        //         }
        //     }
        //
        // }
        // return $r;
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

    /**
     * Query private methods
     *
     * @param string $method method path
     * @param array $request request parameters
     * @return array request result on success
     * @throws KrakenAPIException
     */
    private async Task<JObject> QueryPrivate(string method, Dictionary<string, string>? param, string apiKey,
        string privateKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long nonce = now.ToUnixTimeMilliseconds();

        // build the POST data string
        var postData = new QueryString();
        if (param != null)
        {
            foreach (KeyValuePair<string, string> keyValuePair in param)
            {
                postData.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }
        postData.Add("nonce", nonce.ToString(CultureInfo.InvariantCulture));


        var postDataString = postData.ToString();


        // set API key and sign the message
        var path = "/0/private/" + method;
        var url = "https://api.kraken.com/" + path;

        var hmac256 = new System.Security.Cryptography.HMACSHA256();
        var hmac512 = new System.Security.Cryptography.HMACSHA512(Encoding.UTF8.GetBytes(privateKey));

        // From PHP code: hash_hmac('sha512', $path . hash('sha256', $request['nonce'] . $postdata, true), base64_decode($this->secret), true);

        var unhashed1 = nonce.ToString(CultureInfo.InvariantCulture) + postDataString;
        var hash1 = Encoders.Hex.EncodeData(hmac256.ComputeHash(Encoding.UTF8.GetBytes(unhashed1)));

        var unhashed2 = path + hash1;
        var signature = Encoders.Hex.EncodeData(hmac512.ComputeHash(Encoding.UTF8.GetBytes(unhashed2)));

        var signatureBytes = Encoding.UTF8.GetBytes(signature);
        var apiSign = Convert.ToBase64String(signatureBytes);

        HttpRequestMessage request = new HttpRequestMessage();
        //webRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(new UTF8Encoding(false).GetBytes($"{builder.UserName}:{builder.Password}")));

        request.Method = HttpMethod.Post;
        request.Headers.Add("API-Key", apiKey);
        request.Headers.Add("API-Sign", apiSign);
        //request.Headers.TryAddWithoutValidation("User-Agent", $"BTCPayServer/{GetVersion()}");
        request.Headers.Add("User-Agent", $"BTCPayServer/{GetVersion()}");
        //
        // byte[] byteArray = Encoding.UTF8.GetBytes(postDataString);
        //
        // using var reqStream = request.();
        // reqstream.Write(byteArray, 0, byteArray.Length);
        //
        // using var response = request.GetResponse();

        request.RequestUri = new Uri(url, UriKind.Absolute);
        request.Content =
            new StringContent(postDataString, new UTF8Encoding(false),
                "application/x-www-form-urlencoded; charset=utf-8");

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(0.5));

        var response = await _client.SendAsync(request, cts.Token);
        var r = JObject.Parse(response.Content.ToString());
        return r;

        // make request
        // curl_setopt($this->curl, CURLOPT_URL, $this->url . $path);
        // curl_setopt($this->curl, CURLOPT_POSTFIELDS, $postdata);
        // curl_setopt($this->curl, CURLOPT_HTTPHEADER, $headers);
        // $result = curl_exec($this->curl);
        // if($result===false)
        // throw new KrakenAPIException('CURL error: ' . curl_error($this->curl));
        //
        // // decode results
        // $data = json_decode($result, true);
        // if(!is_array($data))
        // throw new KrakenAPIException('JSON decode error: '.$result);
        //
        // return $data;
    }

    private string GetVersion()
    {
        return typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()
            .Version;
    }
}
