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

    public KrakenClient(HttpClient httpClient, IMemoryCache memoryCache)
    {
        _client = httpClient;
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
        return new[] { "BTC", "LTC" };
    }

    public List<AssetPairData> GetTradableAssetPairs()
    {
        return _memoryCache.GetOrCreate("KrakenTradableAssetPairs", entry =>
        {
            var url = "https://api.kraken.com/0/public/AssetPairs";

            HttpRequestMessage request = CreateHttpClient();
            request.Method = HttpMethod.Get;
            request.RequestUri = new Uri(url, UriKind.Absolute);

            var cancellationToken = CreateCancelationToken();
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
                foreach (KeyValuePair<string, JToken> keyValuePair in resultListObj)
                {
                    var splittablePair = keyValuePair.Value["wsname"]?.ToString();
                    var altname = keyValuePair.Value["altname"]?.ToString();
                    if (splittablePair != null && altname != null)
                    {
                        var parts = splittablePair.Split("/");
                        list.Add(new KrakenAssetPair(ConvertFromKrakenAsset(parts[0]), ConvertFromKrakenAsset(parts[1]),
                            altname));
                    }
                }
            }


            entry.SetAbsoluteExpiration(TimeSpan.FromHours(24));
            entry.Value = list;
            return list;
        });
    }

    private CancellationToken CreateCancelationToken(int timeout = 30)
    {
        var cancellationToken = new CancellationToken();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));
        return cancellationToken;
    }

    private HttpRequestMessage CreateHttpClient()
    {
        HttpRequestMessage request = new();

        // TODO should we advertise ourselves like this? We're doing it on other parts of the code too!
        request.Headers.Add("User-Agent", $"BTCPayServer/{GetVersion()}");

        return request;
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config)
    {
        var krakenConfig = ParseConfig(config);
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

    public async Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config)
    {
        if (paymentMethod == "BTC-OnChain")
        {
            var asset = paymentMethod.Split("-")[0];
            var krakenAsset = ConvertToKrakenAsset(asset);

            var krakenConfig = ParseConfig(config);

            var param = new Dictionary<string, string>();
            param.Add("asset", krakenAsset);
            param.Add("method", "Bitcoin");
            param.Add("new", "true");

            JObject requestResult;
            try
            {
                requestResult = await QueryPrivate("DepositAddresses", param, krakenConfig);
            }
            catch (CustodianApiException ex)
            {
                if (ex.Message == "EFunding:Too many addresses")
                {
                    // We cannot create a new address because there are too many already. Query again and look for an existing address to use.
                    param.Remove("new");
                    requestResult = await QueryPrivate("DepositAddresses", param, krakenConfig);
                }
                else
                {
                    throw;
                }
            }

            var addresses = (JArray)requestResult["result"];

            if (addresses != null)
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

            throw new DepositsUnavailableException("Could not fetch a suitable deposit address.");
        }

        throw new CustodianFeatureNotImplementedException($"Only BTC-OnChain is implemented for {this.GetName()}");
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

    public async Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config)
    {
        // In Kraken, a trade is called an "Order". Don't get confused with a Transaction or a Ledger item!
        var krakenConfig = ParseConfig(config);
        var param = new Dictionary<string, string>();

        // Even though we are looking for an "Order", the parameter is still called "txid", which is confusing, but this is correct.
        param.Add("txid", tradeId);
        try
        {
            var requestResult = await QueryPrivate("QueryOrders", param, krakenConfig);
            var txInfo = requestResult["result"]?[tradeId] as JObject;

            var ledgerEntries = new List<LedgerEntryData>();

            if (txInfo != null)
            {
                var pairString = txInfo["descr"]?["pair"]?.ToString();
                var assetPair = ParseAssetPair(pairString);
                var costInclFee = txInfo["cost"].ToObject<decimal>();
                var feeInQuoteCurrencyEquivalent = txInfo["fee"].ToObject<decimal>();
                var costWithoutFee = costInclFee - feeInQuoteCurrencyEquivalent;
                var qtyBought = txInfo["vol_exec"].ToObject<decimal>();

                ledgerEntries.Add(new LedgerEntryData(assetPair.AssetBought, qtyBought,
                    LedgerEntryData.LedgerEntryType.Trade));
                ledgerEntries.Add(new LedgerEntryData(assetPair.AssetSold, -1 * costWithoutFee,
                    LedgerEntryData.LedgerEntryType.Trade));
                ledgerEntries.Add(new LedgerEntryData(assetPair.AssetSold, -1 * feeInQuoteCurrencyEquivalent,
                    LedgerEntryData.LedgerEntryType.Fee));

                var r = new MarketTradeResult(assetPair.AssetSold, assetPair.AssetBought, ledgerEntries, tradeId);
                return r;
            }
        }
        catch (CustodianApiException exception)
        {
            if (exception.Message == "EOrder:Invalid order")
            {
                // Let it pass, our exception is thrown at the end anyway.
            }
            else
            {
                throw;
            }
        }

        throw new TradeNotFoundException(tradeId);
    }


    public async Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config)
    {
        var pair = FindAssetPair(fromAsset, toAsset, false);
        if (pair == null)
        {
            throw new WrongTradingPairException(fromAsset, toAsset);
        }

        try
        {
            var requestResult = await QueryPublic("Ticker?pair=" + pair.PairCode);

            var bid = requestResult["result"]?.SelectToken("..b[0]");
            var ask = requestResult["result"]?.SelectToken("..a[0]");

            if (bid != null && ask != null)
            {
                var bidDecimal = bid.ToObject<decimal>();
                var askDecimal = ask.ToObject<decimal>();
                return new AssetQuoteResult(fromAsset, toAsset, bidDecimal, askDecimal);
            }
        }
        catch (CustodianApiException e)
        {
        }

        throw new AssetQuoteUnavailableException(pair);
    }

    private KrakenAssetPair ParseAssetPair(string pair)
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


    public async Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config)
    {
        // Make sure qty is positive
        if (qty < 0)
        {
            qty = -1 * qty;
            (fromAsset, toAsset) = (toAsset, fromAsset);
        }

        var assetPair = FindAssetPair(fromAsset, toAsset, true);
        if (assetPair == null)
        {
            throw new WrongTradingPairException(fromAsset, toAsset);
        }

        string orderType;
        if (assetPair.AssetBought == toAsset)
        {
            orderType = "buy";
        }
        else
        {
            orderType = "sell";
            var priceQuote = await GetQuoteForAssetAsync(assetPair.AssetSold, assetPair.AssetBought, config);
            // TODO should we use the Bid or the Ask?
            qty /= priceQuote.Bid;
        }

        var param = new Dictionary<string, string>();
        param.Add("type", orderType);
        param.Add("pair", assetPair.PairCode);
        param.Add("ordertype", "market");
        param.Add("volume", qty.ToStringInvariant());

        var krakenConfig = ParseConfig(config);
        var requestResult = await QueryPrivate("AddOrder", param, krakenConfig);

        // The field is called "txid", but it's an order ID and not a Transaction ID, so we need to be careful! :(
        var orderId = (string)requestResult["result"]?["txid"]?[0];
        var r = await GetTradeInfoAsync(orderId, config);

        return r;
    }

    private KrakenAssetPair FindAssetPair(string fromAsset, string toAsset, bool allowReverse)
    {
        var pairs = GetTradableAssetPairs();
        foreach (var assetPairData in pairs)
        {
            var pair = (KrakenAssetPair)assetPairData;
            if (pair.AssetBought == toAsset && pair.AssetSold == fromAsset)
            {
                return pair;
            }

            if (allowReverse && pair.AssetBought == fromAsset && pair.AssetSold == toAsset)
            {
                return pair;
            }
        }

        return null;
    }

    public async Task<WithdrawResult> WithdrawAsync(string asset, decimal amount, JObject config)
    {
        var krakenConfig = ParseConfig(config);
        var withdrawToAddressName = krakenConfig.WithdrawToAddressName;
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();

        param.Add("asset", krakenAsset);
        param.Add("key", withdrawToAddressName);
        param.Add("amount", amount + "");

        var requestResult = await QueryPrivate("Withdraw", param, krakenConfig);
        
        var refId = (string)requestResult["result"]?["refid"];
        var withdrawalInfo = GetWithdrawalInfoAsync(asset, refId, config).Result;
        var ledgerEntries = new List<LedgerEntryData>();
        var amountExclFee = withdrawalInfo["amount"].ToObject<decimal>();
        var fee = withdrawalInfo["fee"].ToObject<decimal>();
        var withdrawalToAddress = withdrawalInfo["info"].ToString();

        // TODO should we use the time/status?
        // var time = withdrawalInfo["time"]; // Unix timestamp integer. Example: 1644595165
        // var status = withdrawalInfo["status"]; // Example: 'initial'

        ledgerEntries.Add(new LedgerEntryData(asset, -1 * amountExclFee,
            LedgerEntryData.LedgerEntryType.Withdrawal));
        ledgerEntries.Add(new LedgerEntryData(asset, -1 * fee,
            LedgerEntryData.LedgerEntryType.Fee));

        var r = new WithdrawResult(asset, ledgerEntries, refId, withdrawalToAddress);
        return r;
    }

    private async Task<JObject> GetWithdrawalInfoAsync(string asset, string refId, JObject config)
    {
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();
        param.Add("asset", krakenAsset);

        var krakenConfig = ParseConfig(config);
        var withdrawStatusResponse = await QueryPrivate("WithdrawStatus", param, krakenConfig);


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


    private async Task<JObject> QueryPrivate(string method, Dictionary<string, string> param, KrakenConfig config)
    {
        // TODO is this okay? Or should the cancellation token be an input argument?
        var cancellationToken = CreateCancelationToken();

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
        Buffer.BlockCopy(pathBytes, 0, unhashed2, 0, pathBytes.Length);
        Buffer.BlockCopy(hash1, 0, unhashed2, pathBytes.Length, hash1.Length);

        var signature = hmac512.ComputeHash(unhashed2);
        var apiSign = Convert.ToBase64String(signature);

        HttpRequestMessage request = CreateHttpClient();
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

        var error = r["error"];
        if (error is JArray)
        {
            var errorMessageArray = ((JArray)error);
            if (errorMessageArray.Count > 0)
            {
                var errorMessage = errorMessageArray[0].ToString();
                if (errorMessage == "EGeneral:Permission denied")
                {
                    throw new PermissionDeniedCustodianApiException(this);
                }

                // Generic error, we don't know how to better specify
                throw new CustodianApiException(400, "custodian-api-exception", errorMessage);
            }
        }

        return r;
    }

    private async Task<JObject> QueryPublic(string method)
    {
        // TODO is this okay? Or should the cancellation token be an input argument?
        var cancellationToken = CreateCancelationToken();

        var path = "/0/public/" + method;
        var url = "https://api.kraken.com" + path;

        HttpRequestMessage request = CreateHttpClient();
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
                // Generic error, we don't know how to better specify
                throw new CustodianApiException(400, "custodian-api-exception", errorMessageArray[0].ToString());
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

    private KrakenConfig ParseConfig(JObject config)
    {
        return config.ToObject<KrakenConfig>();
    }
}
