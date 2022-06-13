using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Custodians.Kraken.Kraken;

public class KrakenExchange : ICustodian, ICanDeposit, ICanTrade, ICanWithdraw
{
    private readonly HttpClient _client;
    private readonly IMemoryCache _memoryCache;

    public KrakenExchange(HttpClient httpClient, IMemoryCache memoryCache)
    {
        _client = httpClient;
        _memoryCache = memoryCache;
    }

    public string Code
    {
        get => "kraken";
    }

    public string Name
    {
        get => "Kraken";
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
            if (errorMessage is JArray { Count: > 0 } errorMessageArray)
            {
                throw new Exception(errorMessageArray[0].ToString());
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
        // Setting a User-Agent header is required by the Kraken API!
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36");
        return request;
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config,
        CancellationToken cancellationToken)
    {
        var krakenConfig = ParseConfig(config);
        JObject data;
        try
        {
            data = await QueryPrivate("Balance", null, krakenConfig, cancellationToken);
        }
        catch (BadConfigException e)
        {
            throw;
        }
        catch (System.Exception e)
        {
            throw new AssetBalancesUnavailableException(e);
        }

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

    public async Task<Form> GetConfigForm(JObject config, string locale,
        CancellationToken cancellationToken)
    {
        // TODO "locale" is not used yet, but keeping it here so it's clear translation should be done here.

        var krakenConfig = ParseConfig(config);

        var form = new Form();
        var fieldset = new Fieldset { Label = "Connection Details" };

        var apiKeyField = new TextField("API Key", "ApiKey", krakenConfig.ApiKey, true, "Enter your Kraken API Key");
        var privateKeyField = new TextField("Private Key", "PrivateKey", krakenConfig.PrivateKey,
            true,"Enter your Kraken Private Key");

        form.Fieldsets.Add(fieldset);
        fieldset.Fields.Add(apiKeyField);
        fieldset.Fields.Add(privateKeyField);

        // TODO: Instead of showing all, maybe show the withdrawal destinations for all assets that: (1) we have an existing config value for and (2) all assets stored with the custodian.
        var withdrawalFieldset = new Fieldset { Label = "Withdrawal Destinations" };
        form.Fieldsets.Add(withdrawalFieldset);

        var paymentMethods = GetWithdrawablePaymentMethods();

        foreach (var paymentMethod in paymentMethods)
        {
            var value = krakenConfig?.WithdrawToAddressNamePerPaymentMethod?[paymentMethod];
            withdrawalFieldset.Fields.Add(new TextField(paymentMethod,
                "WithdrawToAddressNamePerPaymentMethod[" + paymentMethod + "]",
                value,
                false, "The exact name of the withdrawal destination stored in your Kraken account for " + paymentMethod));
        }
        
        try
        {
            await GetAssetBalancesAsync(config, cancellationToken);
        }
        catch (BadConfigException e)
        {
            foreach (var badConfigKey in e.BadConfigKeys)
            {
                var field = form.GetFieldByName(badConfigKey);
                field.ValidationErrors.Add("Invalid " + field.Label);
            }
            form.TopMessages.Add(new AlertMessage(AlertMessage.AlertMessageType.Danger,"Cannot connect to Kraken. Please check your API and private keys."));
        }

        return form;
    }

    public async Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config,
        CancellationToken cancellationToken)
    {
        if (paymentMethod.Equals("BTC-OnChain", StringComparison.InvariantCulture))
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
                requestResult = await QueryPrivate("DepositAddresses", param, krakenConfig, cancellationToken);
            }
            catch (CustodianApiException ex)
            {
                if (ex.Message.Equals("EFunding:Too many addresses", StringComparison.InvariantCulture))
                {
                    // We cannot create a new address because there are too many already. Query again and look for an existing address to use.
                    param.Remove("new");
                    requestResult = await QueryPrivate("DepositAddresses", param, krakenConfig, cancellationToken);
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

        throw new CustodianFeatureNotImplementedException($"Only BTC-OnChain is implemented for {this.Name}");
    }

    private string ConvertToKrakenAsset(string asset)
    {
        if (asset.Equals("BTC", StringComparison.InvariantCulture))
        {
            return "XBT";
        }

        return asset;
    }

    private string ConvertFromKrakenAsset(string krakenAsset)
    {
        if (krakenAsset.Equals("XBT") || krakenAsset.Equals("XXBT", StringComparison.InvariantCulture))
        {
            return "BTC";
        }

        if (krakenAsset.Equals("XLTC", StringComparison.InvariantCulture))
        {
            return "LTC";
        }

        if (krakenAsset.Equals("ZEUR", StringComparison.InvariantCulture))
        {
            return "EUR";
        }

        if (krakenAsset.Equals("ZUSD", StringComparison.InvariantCulture))
        {
            return "USD";
        }

        return krakenAsset;
    }

    public async Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config,
        CancellationToken cancellationToken)
    {
        // In Kraken, a trade is called an "Order". Don't get confused with a Transaction or a Ledger item!
        var krakenConfig = ParseConfig(config);
        var param = new Dictionary<string, string>();

        // Even though we are looking for an "Order", the parameter is still called "txid", which is confusing, but this is correct.
        param.Add("txid", tradeId);
        try
        {
            var requestResult = await QueryPrivate("QueryOrders", param, krakenConfig, cancellationToken);
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
            if (exception.Message.Equals("EOrder:Invalid order", StringComparison.InvariantCulture))
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


    public async Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config,
        CancellationToken cancellationToken)
    {
        var pair = FindAssetPair(fromAsset, toAsset, false);
        if (pair == null)
        {
            throw new WrongTradingPairException(fromAsset, toAsset);
        }

        try
        {
            var requestResult = await QueryPublic("Ticker?pair=" + pair.PairCode, cancellationToken);

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
                if (krakenAssetPair.PairCode.Equals(pair, StringComparison.InvariantCulture))
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
                    if (onePair.AssetBought.Equals(pairParts[0], StringComparison.InvariantCulture) &&
                        onePair.AssetSold.Equals(pairParts[1], StringComparison.InvariantCulture))
                    {
                        return krakenAssetPair;
                    }
                }
            }
        }

        return null;
    }


    public async Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config,
        CancellationToken cancellationToken)
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
        if (assetPair.AssetBought.Equals(toAsset, StringComparison.InvariantCulture))
        {
            orderType = "buy";
        }
        else
        {
            orderType = "sell";
            var priceQuote =
                await GetQuoteForAssetAsync(assetPair.AssetSold, assetPair.AssetBought, config, cancellationToken);
            qty /= priceQuote.Bid;
        }

        var param = new Dictionary<string, string>();
        param.Add("type", orderType);
        param.Add("pair", assetPair.PairCode);
        param.Add("ordertype", "market");
        param.Add("volume", qty.ToString(CultureInfo.InvariantCulture));

        var krakenConfig = ParseConfig(config);
        var requestResult = await QueryPrivate("AddOrder", param, krakenConfig, cancellationToken);

        // The field is called "txid", but it's an order ID and not a Transaction ID, so we need to be careful! :(
        var orderId = (string)requestResult["result"]?["txid"]?[0];

        // A short delay so Kraken has the time to execute the market order and we don't fetch the details too quickly.
        Thread.Sleep(TimeSpan.FromSeconds(1));

        var r = await GetTradeInfoAsync(orderId, config, cancellationToken);

        return r;
    }

    private KrakenAssetPair FindAssetPair(string fromAsset, string toAsset, bool allowReverse)
    {
        var pairs = GetTradableAssetPairs();
        foreach (var assetPairData in pairs)
        {
            var pair = (KrakenAssetPair)assetPairData;
            if (pair.AssetBought.Equals(toAsset, StringComparison.InvariantCulture) &&
                pair.AssetSold.Equals(fromAsset, StringComparison.InvariantCulture))
            {
                return pair;
            }

            if (allowReverse && pair.AssetBought.Equals(fromAsset, StringComparison.InvariantCulture) &&
                pair.AssetSold.Equals(toAsset, StringComparison.InvariantCulture))
            {
                return pair;
            }
        }

        return null;
    }

    public async Task<WithdrawResult> WithdrawAsync(string paymentMethod, decimal amount, JObject config,
        CancellationToken cancellationToken)
    {
        var krakenConfig = ParseConfig(config);
        var withdrawToAddressNamePerPaymentMethod = krakenConfig.WithdrawToAddressNamePerPaymentMethod;
        var withdrawToAddressName = withdrawToAddressNamePerPaymentMethod[paymentMethod];
        var asset = paymentMethod.Split("-")[0];
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();

        param.Add("asset", krakenAsset);
        param.Add("key", withdrawToAddressName);
        param.Add("amount", amount + "");

        try
        {
            var requestResult = await QueryPrivate("Withdraw", param, krakenConfig, cancellationToken);
            var withdrawalId = (string)requestResult["result"]?["refid"];

            return GetWithdrawalInfoAsync(paymentMethod, withdrawalId, config, cancellationToken).Result;
        }
        catch (CustodianApiException e)
        {
            if (e.Message.Equals("EFunding:Unknown withdraw key", StringComparison.InvariantCulture))
            {
                // This should point the user to the config in the UI, so he can change the withdrawal destination.
                throw new InvalidWithdrawalTargetException(this, paymentMethod, withdrawToAddressName, e);
            }

            // Any other withdrawal issue
            throw new CannotWithdrawException(this, paymentMethod, withdrawToAddressName, e);
        }
    }

    public async Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config,
        CancellationToken cancellationToken)
    {
        var asset = paymentMethod.Split("-")[0];
        var krakenAsset = ConvertToKrakenAsset(asset);
        var param = new Dictionary<string, string>();
        param.Add("asset", krakenAsset);

        var krakenConfig = ParseConfig(config);
        var withdrawStatusResponse = await QueryPrivate("WithdrawStatus", param, krakenConfig, cancellationToken);

        var recentWithdrawals = withdrawStatusResponse["result"];
        foreach (var withdrawal in recentWithdrawals)
        {
            if (withdrawalId.Equals(withdrawal["refid"]?.ToString()))
            {
                var withdrawalInfo = withdrawal.ToObject<JObject>();

                var ledgerEntries = new List<LedgerEntryData>();
                var amountExclFee = withdrawalInfo["amount"].ToObject<decimal>();
                var fee = withdrawalInfo["fee"].ToObject<decimal>();
                var withdrawalToAddress = withdrawalInfo["info"].ToString();

                var timeUnixTimestamp = (int)withdrawalInfo["time"]; // Unix timestamp integer. Example: 1644595165
                var createdTime = DateTimeOffset.FromUnixTimeSeconds(timeUnixTimestamp);
                var statusCode =
                    (string)withdrawalInfo[
                        "status"]; // Examples: "Initial", "Pending", "Settled", "Success" or "Failure"

                var transactionId = (string)withdrawalInfo["txid"]; // This is the transaction ID on the blockchain

                WithdrawalResponseData.WithdrawalStatus status = WithdrawalResponseData.WithdrawalStatus.Unknown;
                if (statusCode.Equals("Initial", StringComparison.InvariantCulture) ||
                    statusCode.Equals("Pending", StringComparison.InvariantCulture) ||
                    statusCode.Equals("Settled", StringComparison.InvariantCulture))
                {
                    // These 3 states are considered "not final", so we map them to "Queued".
                    // Even "Settled" is not really final as per the IFEX financial transaction states (https://github.com/globalcitizen/ifex-protocol/blob/master/draft-ifex-00.txt#L837).
                    status = WithdrawalResponseData.WithdrawalStatus.Queued;
                }
                else if (statusCode.Equals("Success", StringComparison.InvariantCulture))
                {
                    status = WithdrawalResponseData.WithdrawalStatus.Complete;
                }
                else if (statusCode.Equals("Failure", StringComparison.InvariantCulture))
                {
                    status = WithdrawalResponseData.WithdrawalStatus.Failed;
                }

                ledgerEntries.Add(new LedgerEntryData(asset, -1 * amountExclFee,
                    LedgerEntryData.LedgerEntryType.Withdrawal));
                ledgerEntries.Add(new LedgerEntryData(asset, -1 * fee,
                    LedgerEntryData.LedgerEntryType.Fee));

                return new WithdrawResult(paymentMethod, asset, ledgerEntries, withdrawalId, status, createdTime,
                    withdrawalToAddress, transactionId);
            }
        }

        throw new WithdrawalNotFoundException(withdrawalId);
    }

    public string[] GetWithdrawablePaymentMethods()
    {
        // Withdraw is the same as deposit
        return GetDepositablePaymentMethods();
    }


    private async Task<JObject> QueryPrivate(string method, Dictionary<string, string> param, KrakenConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config?.ApiKey))
        {
            throw new BadConfigException(new[] { "ApiKey" });
        }

        if (string.IsNullOrEmpty(config?.PrivateKey))
        {
            throw new BadConfigException(new[] { "PrivateKey" });
        }

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

        byte[] decodedSecret;

        try
        {
            decodedSecret = Convert.FromBase64String(config.PrivateKey);
        }
        catch (FormatException e)
        {
            throw new BadConfigException(new[] { "PrivateKey" });
        }

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
        var responseString = await response.Content.ReadAsStringAsync();
        var r = JObject.Parse(responseString);

        var error = r["error"];
        if (error is JArray)
        {
            var errorMessageArray = ((JArray)error);
            if (errorMessageArray.Count > 0)
            {
                var errorMessage = errorMessageArray[0].ToString();
                if (errorMessage.Equals("EGeneral:Permission denied", StringComparison.InvariantCulture))
                {
                    throw new PermissionDeniedCustodianApiException(this);
                }

                // Generic error, we don't know how to better specify
                throw new CustodianApiException(400, "custodian-api-exception", errorMessage);
            }
        }

        return r;
    }

    private async Task<JObject> QueryPublic(string method, CancellationToken cancellationToken)
    {
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

    public string[] GetDepositablePaymentMethods()
    {
        // TODO add more
        return new[] { "BTC-OnChain", "LTC-OnChain" };
    }

    private KrakenConfig ParseConfig(JObject config)
    {
        return config?.ToObject<KrakenConfig>();
    }
}
