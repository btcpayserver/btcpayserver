using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian;

public class Kraken : ICustodian, ICanDeposit
{
    private static Kraken _instance;

    private Kraken()
    {
    }

    public static Kraken getInstance()
    {
        if (_instance == null)
        {
            _instance = new Kraken();
        }

        return _instance;
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

    public Dictionary<string, decimal> getAssetBalances()
    {
        // TODO use API to get a full list.

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

        throw new NotImplementedException();
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
    private JObject QueryPrivate(string method)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long nonce = now.ToUnixTimeMilliseconds();

        // build the POST data string
        //var params = new Dictionary<string, string>();
        var postDataString = BTCPayServer.Services.Custodian.QueryStringBuilder.BuildQueryString(params);

        // set API key and sign the message
        var path = "/0/private/" + method;

        var hmac256 = new System.Security.Cryptography.HMACSHA256();
        var hmac512 = new System.Security.Cryptography.HMACSHA512(Encoding.UTF8.GetBytes(secretKey));

        // From PHP code: hash_hmac('sha512', $path . hash('sha256', $request['nonce'] . $postdata, true), base64_decode($this->secret), true);

        var unhashed1 = nonce.ToString() + postDataString;
        var hash1 = Encoders.Hex.EncodeData(hmac256.ComputeHash(Encoding.UTF8.GetBytes(unhashed1)));

        var unhashed2 = path + hash1;
        var signature = Encoders.Hex.EncodeData(hmac512.ComputeHash(Encoding.UTF8.GetBytes(unhashed2)));
            
        var signatureBytes = System.Text.Encoding.UTF8.GetBytes(signature);
        var apiSign = System.Convert.ToBase64String(signatureBytes);
            
            $headers = array(
            'API-Key: '.apiKey,
            'API-Sign: '.apiSign
        );

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
}
