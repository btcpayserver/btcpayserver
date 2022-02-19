using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
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
        private readonly IAuthorizationService _authorizationService;

        public GreenfieldCustodianAccountController(CustodianAccountRepository custodianAccountRepository,
            CustodianRegistry custodianRegistry,
            IAuthorizationService authorizationService)
        {
            _custodianAccountRepository = custodianAccountRepository;
            _custodianRegistry = custodianRegistry;
            _authorizationService = authorizationService;
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ListCustodianAccount(string storeId, [FromQuery] bool assetBalances = false, CancellationToken cancellationToken = default)
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
                    var custodian = _custodianRegistry.GetAll()[custodianCode];
                    try
                    {
                        var balances = await custodian.GetAssetBalancesAsync(custodianAccountResponse.Config, cancellationToken);
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


        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId,
            [FromQuery] bool assetBalances = false, CancellationToken cancellationToken = default)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            }

            var custodianAccountData = _custodianAccountRepository.FindById(accountId).Result;
            if (custodianAccountData == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var custodianAccount = ToModelWithAssets(custodianAccountData);
            if (custodianAccount != null && assetBalances)
            {
                // TODO this is copy paste from above. Maybe put it in a method? Can be use ToModel for this? Not sure how to do it...
                var custodianCode = custodianAccount.CustodianCode;
                var custodian = _custodianRegistry.GetAll()[custodianCode];
                var balances = await custodian.GetAssetBalancesAsync(custodianAccount.Config, cancellationToken);
                custodianAccount.AssetBalances = balances;
            }

            return Ok(custodianAccount);
        }

        private bool CanSeeCustodianAccountConfig()
        {
            return _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanManageCustodianAccounts)).Result.Succeeded;
        }

        private CustodianAccountResponse ToModelWithAssets(CustodianAccountData custodianAccount)
        {
            var r = new CustodianAccountResponse { Id = custodianAccount.Id, CustodianCode = custodianAccount.CustodianCode, StoreId = custodianAccount.StoreId };
            if (CanSeeCustodianAccountConfig())
            {
                // Only show the "config" field if the user can create or manage the Custodian Account, because config contains sensitive information (API key, etc).
                r.Config = custodianAccount.GetBlob().config;
            }

            return r;
        }

        private CustodianAccountDataClient ToModel(CustodianAccountData custodianAccount)
        {
            var r = new CustodianAccountDataClient { Id = custodianAccount.Id, Name = custodianAccount.Name, CustodianCode = custodianAccount.CustodianCode, StoreId = custodianAccount.StoreId };
            if (CanSeeCustodianAccountConfig())
            {
                // Only show the "config" field if the user can create or manage the Custodian Account, because config contains sensitive information (API key, etc).
                r.Config = custodianAccount.GetBlob().config;
            }

            return r;
        }

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts")]
        [Authorize(Policy = Policies.CanManageCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountRequest request)
        {
            request ??= new CreateCustodianAccountRequest();

            // TODO this may throw an exception if custodian is not found. How do I make this better?
            var custodian = _custodianRegistry.GetAll()[request.CustodianCode];

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?

            // Use the name provided or if none provided use the name of the custodian.
            string name = string.IsNullOrEmpty(request.Name) ? custodian.GetName() : request.Name;

            var custodianAccount = new CustodianAccountData() { CustodianCode = custodian.GetCode(), Name = name, StoreId = storeId, };
            var newBlob = new CustodianAccountData.CustodianAccountBlob();
            newBlob.config = request.Config;
            custodianAccount.SetBlob(newBlob);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(ToModel(custodianAccount));
        }


        [HttpPut("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}")]
        [Authorize(Policy = Policies.CanManageCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateCustodianAccount(string storeId, string accountId,
            CreateCustodianAccountRequest request)
        {
            request ??= new CreateCustodianAccountRequest();

            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.GetAll();

            // TODO if the custodian with the desired code does not exist, this will throw an error
            var custodian = allCustodians[request.CustodianCode];

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?
            custodianAccount.CustodianCode = custodian.GetCode();
            custodianAccount.StoreId = storeId;
            custodianAccount.Name = request.Name;

            var newBlob = new CustodianAccountData.CustodianAccountBlob();
            newBlob.config = request.Config;
            custodianAccount.SetBlob(newBlob);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(ToModel(custodianAccount));
        }

        [HttpDelete("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}")]
        [Authorize(Policy = Policies.CanManageCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> DeleteCustodianAccount(string storeId, string accountId)
        {
            var isDeleted = await _custodianAccountRepository.Remove(accountId, storeId);
            if (isDeleted)
            {
                return Ok();
            }

            return this.CreateAPIError(404, "custodian-account-not-found",
                $"Could not find the custodian account");
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/addresses/{paymentMethod}")]
        [Authorize(Policy = Policies.CanDepositToCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetDepositAddress(string storeId, string accountId, string paymentMethod, CancellationToken cancellationToken = default)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.GetAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];
            var config = custodianAccount.GetBlob().config;

            if (custodian is ICanDeposit depositableCustodian)
            {
                try
                {
                    var result = await depositableCustodian.GetDepositAddressAsync(paymentMethod, config, cancellationToken);
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

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/market")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> TradeMarket(string storeId, string accountId,
            TradeRequestData request, CancellationToken cancellationToken = default)
        {
            // TODO add SATS check everywhere. We cannot change to 'BTC' ourselves because the qty / price would be different too.
            if ("SATS".Equals(request.FromAsset) || "SATS".Equals(request.ToAsset))
            {
                return this.CreateAPIError(400, "use-synonym-asset",
                    $"Please use 'BTC' instead of 'SATS'.");
            }

            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.GetAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                decimal Qty;
                if (request.Qty.EndsWith("%", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Qty is a percentage of current holdings
                    var config = custodianAccount.GetBlob().config;
                    var balances = custodian.GetAssetBalancesAsync(config, cancellationToken).Result;
                    var fromAssetBalance = balances[request.FromAsset];
                    var priceQuote =
                        await tradableCustodian.GetQuoteForAssetAsync(request.ToAsset, request.FromAsset, config, cancellationToken);
                    Qty = fromAssetBalance / priceQuote.Ask;
                }
                else
                {
                    // Qty is an exact amount
                    Qty = Decimal.Parse(request.Qty, CultureInfo.InvariantCulture);
                    
                }

                try
                {
                    var result = await tradableCustodian.TradeMarketAsync(request.FromAsset, request.ToAsset, Qty,
                        custodianAccount.GetBlob().config, cancellationToken);

                    return Ok(ToModel(result, accountId, custodianAccount.CustodianCode));
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "market-trade-not-supported",
                $"Placing market orders on \"{custodian.GetName()}\" is not supported.");
        }

        private MarketTradeResponseData ToModel(MarketTradeResult marketTrade, string accountId, string custodianCode)
        {
            return new MarketTradeResponseData(marketTrade.FromAsset, marketTrade.ToAsset, marketTrade.LedgerEntries, marketTrade.TradeId, accountId, custodianCode);
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/quote")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeQuote(string storeId, string accountId, [FromQuery] string fromAsset, [FromQuery] string toAsset, CancellationToken cancellationToken = default)
        {
            // TODO add SATS check everywhere. We cannot change to 'BTC' ourselves because the qty / price would be different too.
            if ("SATS".Equals(fromAsset) || "SATS".Equals(toAsset))
            {
                return this.CreateAPIError(400, "use-asset-synonym",
                    $"Please use 'BTC' instead of 'SATS'.");
            }

            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.GetAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                try
                {
                    var priceQuote = await tradableCustodian.GetQuoteForAssetAsync(fromAsset, toAsset, custodianAccount.GetBlob().config, cancellationToken);
                    return Ok(new TradeQuoteResponseData(priceQuote.FromAsset, priceQuote.ToAsset, priceQuote.Bid, priceQuote.Ask));
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "getting-quote-not-supported",
                $"Getting a price quote on \"{custodian.GetName()}\" is not supported.");
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/{tradeId}")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeInfo(string storeId, string accountId, string tradeId, CancellationToken cancellationToken = default)
        {
            // TODO these couple of lines are used a lot. How do we DRY?
            var custodianAccount = await _custodianAccountRepository.FindById(accountId);
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }

            var allCustodians = _custodianRegistry.GetAll();
            var custodian = allCustodians[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                try
                {
                    var result = await tradableCustodian.GetTradeInfoAsync(tradeId, custodianAccount.GetBlob().config, cancellationToken);
                    if (result == null)
                    {
                        return this.CreateAPIError(404, "trade-not-found",
                            $"Could not find the the trade with ID {tradeId} on {custodianAccount.Name}");
                    }
                    return Ok(ToModel(result, accountId, custodianAccount.CustodianCode));
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "fetching-trade-info-not-supported",
                $"Fetching past trade info on \"{custodian.GetName()}\" is not supported.");
        }


        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals")]
        [Authorize(Policy = Policies.CanWithdrawFromCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateWithdrawal(string storeId, string accountId,
            WithdrawRequestData request, CancellationToken cancellationToken = default)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found",
                    $"Could not find the custodian account");
            }
            var custodian = _custodianRegistry.GetAll()[custodianAccount.CustodianCode];

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                try
                {
                    var withdrawResult =
                        await withdrawableCustodian.WithdrawAsync(request.PaymentMethod, request.Qty, custodianAccount.GetBlob().config, cancellationToken);
                    var result = new WithdrawalResponseData(withdrawResult.PaymentMethod, withdrawResult.Asset, withdrawResult.LedgerEntries,
                        withdrawResult.WithdrawalId, accountId, custodian.GetCode(), withdrawResult.Status, withdrawResult.TargetAddress, withdrawResult.TransactionId);
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


        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals/{paymentMethod}/{withdrawalId}")]
        [Authorize(Policy = Policies.CanWithdrawFromCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetWithdrawalInfo(string storeId, string accountId, string paymentMethod, string withdrawalId, CancellationToken cancellationToken = default)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
            if (custodianAccount == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found", $"Could not find the custodian account");
            }
            var custodian = _custodianRegistry.GetAll()[custodianAccount.CustodianCode];

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                try
                {
                    var withdrawResult = await withdrawableCustodian.GetWithdrawalInfoAsync(paymentMethod, withdrawalId, custodianAccount.GetBlob().config, cancellationToken);
                    if (withdrawResult == null)
                    {
                        return this.CreateAPIError(404, "withdrawal-not-found", "The withdrawal was not found.");
                    }
                    var result = new WithdrawalResponseData(withdrawResult.PaymentMethod, withdrawResult.Asset, withdrawResult.LedgerEntries,
                        withdrawResult.WithdrawalId, accountId, custodian.GetCode(), withdrawResult.Status, withdrawResult.TargetAddress, withdrawResult.TransactionId);
                    return Ok(result);
                }
                catch (CustodianApiException e)
                {
                    return CreateCustodianApiError(e);
                }
            }

            return this.CreateAPIError(400, "fetching-withdrawal-info-not-supported",
                $"Fetching withdrawal information is not supported for \"{custodian.GetName()}\".");
        }
    }

}
