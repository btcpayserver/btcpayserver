using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian.Client.Exception;
using ExchangeSharp;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian.Client.Kraken;

public class KrakenClient : ICustodian, ICanDeposit, ICanTrade, ICanWithdraw
{
    private readonly HttpClient _client;
    private readonly IMemoryCache _memoryCache;

    public KrakenClient(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
    {
        _client = httpClientFactory.CreateClient();
        _memoryCache = memoryCache;
    }

    public string GetCode()
    {
        return "kraken";
    }

    public string GetName()
    {
        return "Kraken";
    }

    public string[] GetSupportedAssets()
    {
        // TODO use API to get a full list.
        return new string[] { "BTC", "LTC" };
    }

    public List<AssetPairData> GetTradableAssetPairs()
    {
        return _memoryCache.GetOrCreate("KrakenTradableAssetPairs", entry =>
        {
            var url = "https://api.kraken.com/0/public/AssetPairs";

            HttpRequestMessage request = createHttpClient();
            request.Method = HttpMethod.Get;
            request.RequestUri = new Uri(url, UriKind.Absolute);

            var cancellationToken = createCancelationToken();
            var response = _client.Send(request, cancellationToken);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var data = JObject.Parse(responseString);

            var errorMessage = data["error"];
            if (errorMessage is JArray errorMessageArray)
            {
                if (errorMessageArray.Count > 0)
                {
                    throw new APIException(errorMessageArray[0].ToString());
                }
            }

            var list = new List<AssetPairData>();
            var resultList = data["result"];
            if (resultList is JObject resultListObj)
            {
                foreach (KeyValuePair<string, JToken?> keyValuePair in resultListObj)
                {
                    var splittablePair = keyValuePair.Value["wsname"].ToString();
                    var parts = splittablePair.Split("/");
                    list.Add(new KrakenAssetPair(ConvertFromKrakenAsset(parts[0]), ConvertFromKrakenAsset(parts[1]),
                        keyValuePair.Key));
                }
            }


            entry.SetAbsoluteExpiration(TimeSpan.FromHours(24));
            entry.Value = list;
            return list;
        });
    }

    private CancellationToken createCancelationToken(int timeout = 30)
    {
        var cancellationToken = new CancellationToken();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));
        return cancellationToken;
    }

    private HttpRequestMessage createHttpClient()
    {
        HttpRequestMessage request = new HttpRequestMessage();

        // TODO should we advertise ourselves like this? We're doing it on other parts of the code too!
        request.Headers.Add("User-Agent", $"BTCPayServer/{GetVersion()}");

        return request;
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalances(JObject config)
    {
        var krakenConfig = parseConfig(config);
        var data = await QueryPrivate("Balance", null, krakenConfig);
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
                    if (amount > 0)
                    {
                        var asset = ConvertFromKrakenAsset(keyValuePair.Key);
                        r.Add(asset, amount);
                    }
                }
            }

            return r;
        }

        return null;
    }

    public async Task<DepositAddressData> GetDepositAddress(string paymentMethod, JObject config)
    {
        if (paymentMethod == "BTC-OnChain")
        {
            var asset = paymentMethod.Split("-")[0];
            var krakenAsset = ConvertToKrakenAsset(asset);

            var krakenConfig = parseConfig(config);

            var param = new Dictionary<string, string>();
            param.Add("asset", krakenAsset);
            param.Add("method", "Bitcoin");
            param.Add("new", "true");
            var requestResult = await QueryPrivate("DepositAddresses", param, krakenConfig);
            var addresses = requestResult["result"] as JArray;

            foreach (var address in addresses)
            {
                bool isNew = (bool)address["new"];

                // TODO checking expiry timestamp could be done better
                bool isNotExpired = (int)address["expiretm"] == 0;

                if (isNew && isNotExpired)
                {
                    var result = new DepositAddressData();
                    result.Address = address["address"]?.ToString();
                    return result;
                }
            }

            throw new CustodianApiException("Could not fetch a suitable deposit address.");
        }

        throw new CustodianApiException("Only BTC-OnChain is implemented at this time.");
    }

    private string ConvertToKrakenAsset(string asset)
    {
        if (asset == "BTC")
        {
            return "XBT";
        }

        return asset;
    }

    private string ConvertFromKrakenAsset(string krakenAsset)
    {
        if (krakenAsset == "XBT" || krakenAsset == "XXBT")
        {
            return "BTC";
        }

        if (krakenAsset == "XLTC")
        {
            return "LTC";
        }

        if (krakenAsset == "ZEUR")
        {
            return "EUR";
        }

        if (krakenAsset == "ZUSD")
        {
            return "USD";
        }

        return krakenAsset;
    }

    public async Task<MarketTradeResult> GetTradeInfo(string tradeId, JObject config)
    {
        var krakenConfig = parseConfig(config);
        var param = new Dictionary<string, string>();
        param.Add("txid", tradeId);
        var requestResult = await QueryPrivate("QueryTrades", param, krakenConfig);
        var txInfo = requestResult["result"]?[tradeId] as JObject;

        var ledgerEntries = new List<LedgerEntryData>();

        if (txInfo != null)
        {
            var pairString = txInfo["pair"].ToString();
            var assetPair = parseAssetPair(pairString);
            var costInclFee = txInfo["cost"].ToObject<decimal>();
            var feeInQuoteCurrencyEquivalent = txInfo["fee"].ToObject<decimal>();
            var costWithoutFee = costInclFee - feeInQuoteCurrencyEquivalent;
            var qtyBought = txInfo["vol"].ToObject<decimal>();

            ledgerEntries.Add(new LedgerEntryData(assetPair.AssetBought, qtyBought,
                LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData(assetPair.AssetSold, -1 * costWithoutFee,
                LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData(assetPair.AssetSold, -1 * feeInQuoteCurrencyEquivalent,
                LedgerEntryData.LedgerEntryType.Fee));

            var r = new MarketTradeResult(assetPair.AssetSold, assetPair.AssetBought, ledgerEntries, tradeId);
            return r;
        }

        throw new TradeNotFoundException(tradeId);
    }

    public async Task<decimal> GetBidForAsset(string toAsset, string fromAsset, JObject config)
    {
        var krakenAssetPair = ConvertToKrakenAsset(toAsset) + ConvertToKrakenAsset(fromAsset);
        var requestResult = await QueryPublic("Ticker?pair=" + krakenAssetPair);
        // TODO better NULL detection & error handling
        var bid = requestResult["result"].First.First["b"][0].ToObject<decimal>();
        return bid;
    }

    private KrakenAssetPair parseAssetPair(string pair)
    {
        // 1. Check if this is an exact match with a pair we know
        var pairs = GetTradableAssetPairs();
        foreach (var onePair in pairs)
        {
            if (onePair is KrakenAssetPair krakenAssetPair)
            {
                if (krakenAssetPair.PairCode == pair)
                {
                    return krakenAssetPair;
                }
            }
        }

        // 2. Check if this is a pair we can match
        var pairParts = pair.Split("/");
        if (pairParts.Length == 2)
        {
            foreach (var onePair in pairs)
            {
                if (onePair is KrakenAssetPair krakenAssetPair)
                {
                    if (onePair.AssetBought == pairParts[0] && onePair.AssetSold == pairParts[1])
                    {
                        return krakenAssetPair;
                    }
                }
            }
        }

        return null;
    }


    public async Task<MarketTradeResult> TradeMarket(string fromAsset, string toAsset, decimal qty, JObject config)
    {
        var krakenFromAsset = ConvertToKrakenAsset(fromAsset);
        var krakenToAsset = ConvertToKrakenAsset(toAsset);

        // If the pair does not exist, Kraken's API will fail and that's fine.
        var assetPair = krakenToAsset+krakenFromAsset;

        var param = new Dictionary<string, string>();
        var qtyPositive = qty;
        if (qty > 0)
        {
            param.Add("type", "buy");
        }
        else
        {
            qtyPositive = -1 * qty;
            param.Add("type", "sell");
        }

        param.Add("pair", assetPair);
        param.Add("ordertype", "market");
        param.Add("volume", qtyPositive.ToStringInvariant());

        var krakenConfig = parseConfig(config);
        var requestResult = await QueryPrivate("AddOrder", param, krakenConfig);
        
        var txid = (string)requestResult["result"]?["txid"]?[0];
        var r = await GetTradeInfo(txid, config);
        return r;
    }

    public async Task<WithdrawResult> Withdraw(string asset, decimal amount, JObject config)
    {
        var krakenConfig = parseConfig(config);
        var withdrawToAddressName = krakenConfig.WithdrawToAddressName;
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();

        param.Add("asset", krakenAsset);
        param.Add("key", withdrawToAddressName);
        param.Add("amount", amount + "");

        var requestResult = await QueryPrivate("Withdraw", param, krakenConfig);

        // TODO test this
        var refId = (string)requestResult["result"]?["refid"];
        var withdrawalInfo = GetWithdrawalInfo(asset, refId, config).Result;

        var ledgerEntries = new List<LedgerEntryData>();
        //ledgerEntries.Add(new LedgerEntryData(asset, 0));

        var r = new WithdrawResult(asset, ledgerEntries, refId);
        return r;
    }

    private async Task<JObject> GetWithdrawalInfo(string asset, string refId, JObject config)
    {
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();
        param.Add("asset", krakenAsset);

        var krakenConfig = parseConfig(config);
        var withdrawStatusResponse = await QueryPrivate("WithdrawStatus", param, krakenConfig);

        var error = withdrawStatusResponse["error"];
        var recentWithdrawals = withdrawStatusResponse["result"];
        foreach (var withdrawal in recentWithdrawals)
        {
            if (withdrawal["refid"]?.ToString() == refId)
            {
                return withdrawal.ToObject<JObject>();
            }
        }

        return null;
    }


    private async Task<JObject> QueryPrivate(string method, Dictionary<string, string>? param, KrakenConfig config)
    {
        // TODO is this okay? Or should the cancellation token be an input argument?
        var cancellationToken = createCancelationToken();

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
        var decodedSecret = Convert.FromBase64String(config.PrivateKey);

        var sha256 = SHA256.Create();
        var hmac512 = new HMACSHA512(decodedSecret);

        var unhashed1 = nonce.ToString(CultureInfo.InvariantCulture) + postDataString;
        var hash1 = sha256.ComputeHash(Encoding.UTF8.GetBytes(unhashed1));
        var pathBytes = Encoding.UTF8.GetBytes(path);

        byte[] unhashed2 = new byte[path.Length + hash1.Length];
        System.Buffer.BlockCopy(pathBytes, 0, unhashed2, 0, pathBytes.Length);
        System.Buffer.BlockCopy(hash1, 0, unhashed2, pathBytes.Length, hash1.Length);

        var signature = hmac512.ComputeHash(unhashed2);
        var apiSign = Convert.ToBase64String(signature);

        HttpRequestMessage request = createHttpClient();
        request.Method = HttpMethod.Post;
        request.Headers.Add("API-Key", config.ApiKey);
        request.Headers.Add("API-Sign", apiSign);
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
                throw new CustodianApiException(errorMessageArray[0].ToString());
            }
        }

        return r;
    }

    private async Task<JObject> QueryPublic(string method)
    {
        // TODO is this okay? Or should the cancellation token be an input argument?
        var cancellationToken = createCancelationToken();

        var path = "/0/public/" + method;
        var url = "https://api.kraken.com" + path;

        HttpRequestMessage request = createHttpClient();
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(url, UriKind.Absolute);

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
                throw new CustodianApiException(errorMessageArray[0].ToString());
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

    public string[] GetDepositablePaymentMethods()
    {
        // TODO add more
        return new[] { "BTC-OnChain", "LTC-OnChain" };
    }

    private KrakenConfig parseConfig(JObject config)
    {
        return config.ToObject<KrakenConfig>();
    }
}
