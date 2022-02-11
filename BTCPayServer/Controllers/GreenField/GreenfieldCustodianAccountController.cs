using System;
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian;
using BTCPayServer.Services.Custodian.Client;
using BTCPayServer.Services.Custodian.Client.Exception;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using CustodianAccountData = BTCPayServer.Data.CustodianAccountData;
using CustodianAccountDataClient = BTCPayServer.Client.Models.CustodianAccountData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldCustodianAccountController : ControllerBase
    {
        private readonly CustodianRegistry _custodianRegistry;
        private readonly CustodianAccountRepository _custodianAccountRepository;

        public GreenfieldCustodianAccountController(CustodianAccountRepository custodianAccountRepository,
            CustodianRegistry custodianRegistry)
        {
            _custodianAccountRepository = custodianAccountRepository;
            _custodianRegistry = custodianRegistry;
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ListCustodianAccount(string storeId, [FromQuery] bool assetBalances = false)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            }

            var custodianAccounts = _custodianAccountRepository.FindByStoreId(storeId);
            var r = custodianAccounts.Result;

            CustodianAccountDataClient[] responses = new CustodianAccountDataClient[r.Length];

            for (int i = 0; i < r.Length; i++)
            {
                var custodianAccountData = r[i];

                if (assetBalances)
                {
                    var custodianAccountResponse = ToModelWithAssets(custodianAccountData);
                    var custodianCode = custodianAccountResponse.CustodianCode;
                    var custodian = _custodianRegistry.getAll()[custodianCode];
                    try
                    {
                        var balances = await custodian.GetAssetBalancesAsync(custodianAccountResponse.Config);
                        custodianAccountResponse.AssetBalances = balances;
                        responses[i] = custodianAccountResponse;
                    }
                    catch (CustodianApiException e)
                    {
                        return CreateCustodianApiError(e);
                    }
                }
                else
                {
                    var custodianAccountResponse = ToModel(custodianAccountData);
                    responses[i] = custodianAccountResponse;
                }
            }

            return Ok(responses);
        }


        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId,
            [FromQuery] bool assetBalances = false)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            }

            var custodianAccountData = _custodianAccountRepository.FindById(accountId).Result;
            if (custodianAccountData == null)
            {
                return NotFound();
            }

            var custodianAccount = ToModelWithAssets(custodianAccountData);
            if (custodianAccount != null && assetBalances)
            {
                // TODO this is copy paste from above. Maybe put it in a method? Can be use ToModel for this? Not sure how to do it...
                var custodianCode = custodianAccount.CustodianCode;
                var custodian = _custodianRegistry.getAll()[custodianCode];
                var balances = await custodian.GetAssetBalancesAsync(custodianAccount.Config);
                custodianAccount.AssetBalances = balances;
            }

            return Ok(custodianAccount);
        }

        private CustodianAccountResponse ToModelWithAssets(CustodianAccountData custodianAccount)
        {
            var r = new CustodianAccountResponse();
            r.Id = custodianAccount.Id;
            r.CustodianCode = custodianAccount.CustodianCode;
            r.StoreId = custodianAccount.StoreId;
            r.Config = custodianAccount.GetBlob().config;
            return r;
        }

        private CustodianAccountDataClient ToModel(CustodianAccountData custodianAccount)
        {
            var r = new CustodianAccountDataClient();
            r.Id = custodianAccount.Id;
            r.CustodianCode = custodianAccount.CustodianCode;
            r.StoreId = custodianAccount.StoreId;
            r.Config = custodianAccount.GetBlob().config;
            return r;
        }

        [HttpPost("~/api/v1/store/{storeId}/custodian-account")]
        [Authorize(Policy = Policies.CanCreateCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountRequest request)
        {
            request ??= new CreateCustodianAccountRequest();

            // TODO this may throw an exception if custodian is not found. How do I make this better?
            var custodian = _custodianRegistry.getAll()[request.CustodianCode];

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?

            var custodianAccount = new CustodianAccountData() { CustodianCode = custodian.GetCode(), StoreId = storeId, };
            var newBlob = new CustodianAccountData.CustodianAccountBlob();
            newBlob.config = request.Config;
            custodianAccount.SetBlob(newBlob);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(ToModel(custodianAccount));
        }


        [HttpPut("~/api/v1/store/{storeId}/custodian-account/{accountId}")]
        [Authorize(Policy = Policies.CanModifyCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateCustodianAccount(string storeId, string accountId,
            UpdateCustodianAccountRequest request)
        {
            request ??= new UpdateCustodianAccountRequest();

            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.getAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?
            custodianAccount.CustodianCode = custodian.GetCode();
            custodianAccount.StoreId = storeId;

            var newBlob = new CustodianAccountData.CustodianAccountBlob();
            newBlob.config = request.Config;
            custodianAccount.SetBlob(newBlob);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(ToModel(custodianAccount));
        }

        [HttpDelete("~/api/v1/store/{storeId}/custodian-account/{accountId}")]
        [Authorize(Policy = Policies.CanModifyCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> DeleteCustodianAccount(string storeId, string accountId)
        {
            var isDeleted = await _custodianAccountRepository.Remove(accountId, storeId);
            if (isDeleted)
            {
                return Ok();
            }

            return NotFound();
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}/{paymentMethod}/address")]
        [Authorize(Policy = Policies.CanDepositToCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetDepositAddress(string storeId, string accountId, string paymentMethod)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.getAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];
            var config = custodianAccount.GetBlob().config;

            if (custodian is ICanDeposit depositableCustodian)
            {
                try
                {
                    var result = await depositableCustodian.GetDepositAddressAsync(paymentMethod, config);
                    return Ok(result);
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "deposit-payment-method-not-supported",
                $"Deposits to \"{custodian.GetName()}\" are not supported using \"{paymentMethod}\".");
        }

        private IActionResult CreateCustodianApiError(CustodianApiException exception)
        {
            var r = this.CreateAPIError(exception.HttpStatus, exception.Code, exception.Message);
            return r;
        }

        [HttpPost("~/api/v1/store/{storeId}/custodian-account/{accountId}/trade/market")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Trade(string storeId, string accountId,
            TradeRequestData request)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.getAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                decimal Qty;
                if (request.Qty.EndsWith("%", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Qty is a percentage of current holdings
                    var config = custodianAccount.GetBlob().config;
                    var balances = custodian.GetAssetBalancesAsync(config).Result;
                    var qtyToSell = balances[request.FromAsset];
                    var priceQuote =
                        await tradableCustodian.GetQuoteForAssetAsync(request.ToAsset, request.FromAsset, config);
                    // TODO should we use the Bid or the Ask?
                    Qty = qtyToSell / priceQuote.Bid;
                }
                else
                {
                    // Qty is an exact amount
                    Qty = Decimal.Parse(request.Qty, CultureInfo.InvariantCulture);
                    // TODO better error handling
                }

                try
                {
                    var result = await tradableCustodian.TradeMarketAsync(request.FromAsset, request.ToAsset, Qty,
                        custodianAccount.GetBlob().config);
                    return Ok(result);
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "market-trade-not-supported",
                $"Placing market orders on \"{custodian.GetName()}\" is not supported.");
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}/trade/quote")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeQuote(string storeId, string accountId, [FromQuery] string fromAsset, [FromQuery] string toAsset)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.getAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                try
                {
                    var priceQuote = await tradableCustodian.GetQuoteForAssetAsync(fromAsset, toAsset, custodianAccount.GetBlob().config);
                    return Ok(new TradeQuoteResult(priceQuote.FromAsset, priceQuote.ToAsset, priceQuote.Bid, priceQuote.Ask));
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "getting-quote-not-supported",
                $"Getting a price quote on \"{custodian.GetName()}\" is not supported.");
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}/trade/{tradeId}")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeInfo(string storeId, string accountId, string tradeId)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.getAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                try
                {
                    var result = await tradableCustodian.GetTradeInfoAsync(tradeId, custodianAccount.GetBlob().config);
                    return Ok(result);
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "fetching-trade-info-not-supported",
                $"Fetching past trade info on \"{custodian.GetName()}\" is not supported.");
        }


        [HttpPost("~/api/v1/store/{storeId}/custodian-account/{accountId}/withdraw")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Withdraw(string storeId, string accountId,
            WithdrawRequestData request)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
            var custodian = _custodianRegistry.getAll()[custodianAccount.CustodianCode];

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                try
                {
                    var withdrawResult =
                        await withdrawableCustodian.WithdrawAsync(request.Asset, request.Qty, custodianAccount.GetBlob().config);
                    var result = new WithdrawResultData(withdrawResult.Asset, withdrawResult.LedgerEntries,
                        withdrawResult.WithdrawalId, accountId, custodian.GetCode());
                    return Ok(result);
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "withdrawals-not-supported",
                $"Withdrawals are not supported for \"{custodian.GetName()}\".");
        }
    }

    public class TradeQuoteResult
    {
        public decimal Bid { get; }
        public decimal Ask { get; }
        public string ToAsset { get; }
        public string FromAsset { get; }

        public TradeQuoteResult(string fromAsset, string toAsset, decimal bid, decimal ask)
        {
            this.FromAsset = fromAsset;
            this.ToAsset = toAsset;
            this.Bid = bid;
            this.Ask = ask;
        }
    }


    public class TradeRequestData
    {
        public string FromAsset { set; get; }
        public string ToAsset { set; get; }
        public string Qty { set; get; }
    }

    public class WithdrawRequestData
    {
        public string Asset { set; get; }
        public decimal Qty { set; get; }
    }
}
