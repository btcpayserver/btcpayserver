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

            CustodianAccountResponse[] responses = new CustodianAccountResponse[r.Length];
            for (int i = 0; i < r.Length; i++)
            {
                var custodianAccountData = r[i];
                var custodianAccountResponse = ToModelWithAssets(custodianAccountData);

                if (assetBalances)
                {
                    var custodianCode = custodianAccountResponse.CustodianCode;
                    var custodian = _custodianRegistry.getAll()[custodianCode];
                    var balances = await custodian.GetAssetBalances(custodianAccountResponse.Config);
                    custodianAccountResponse.AssetBalances = balances;
                }

                responses[i] = custodianAccountResponse;
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
                var balances = await custodian.GetAssetBalances(custodianAccount.Config);
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

            var custodianAccount = new CustodianAccountData()
            {
                CustodianCode = custodian.GetCode(), StoreId = storeId,
            };
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

            // TODO this may throw an exception if custodian is not found. How do I make this better?
            var custodian = _custodianRegistry.getAll()[request.CustodianCode];

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?

            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
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
            var custodianAccount = _custodianAccountRepository.FindById(accountId);
            var custodian = _custodianRegistry.getAll()[custodianAccount.Result.CustodianCode];
            var config = custodianAccount.Result.GetBlob().config;

            if (custodian is ICanDeposit depositableCustodian)
            {
                try
                {
                    var result = await depositableCustodian.GetDepositAddress(paymentMethod, config);
                    return Ok(result);
                }
                catch (CustodianApiException exception)
                {
                    return this.CreateAPIError(400, "api-exception", exception.Message);
                }
            }

            return this.CreateAPIError(400, "deposit-payment-method-not-supported",
                $"Deposits to \"{custodian.GetName()}\" are not supported using \"{paymentMethod}\".");
        }

        [HttpPost("~/api/v1/store/{storeId}/custodian-account/{accountId}/trade/market")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Trade(string storeId, string accountId,
            TradeRequestData request)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
            var custodian = _custodianRegistry.getAll()[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                decimal Qty;
                if (request.Qty.EndsWith("%", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Qty is a percentage of current holdings
                    var config = custodianAccount.GetBlob().config;
                    var balances = custodian.GetAssetBalances(config).Result;
                    var qtyToSell = balances[request.FromAsset];
                    var currentPrice =
                        await tradableCustodian.GetBidForAsset(request.ToAsset, request.FromAsset, config);
                    Qty = qtyToSell / currentPrice;
                }
                else
                {
                    // Qty is an exact amount
                    Qty = Decimal.Parse(request.Qty, CultureInfo.InvariantCulture);
                    // TODO better error handling
                }

                var result = await tradableCustodian.TradeMarket(request.FromAsset, request.ToAsset, Qty,
                    custodianAccount.GetBlob().config);
                return Ok(result);
            }

            return this.CreateAPIError(400, "market-trade-not-supported",
                $"Placing market orders on \"{custodian.GetName()}\" is not supported.");
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}/trade/{tradeId}")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeInfo(string storeId, string accountId, string tradeId)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId).Result;
            var custodian = _custodianRegistry.getAll()[custodianAccount.CustodianCode];

            if (custodian is ICanTrade tradableCustodian)
            {
                var result = await tradableCustodian.GetTradeInfo(tradeId, custodianAccount.GetBlob().config);
                return Ok(result);
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
                var withdrawResult =
                    await withdrawableCustodian.Withdraw(request.Asset, request.Qty, custodianAccount.GetBlob().config);
                var result = new WithdrawResultData(withdrawResult.Asset, withdrawResult.LedgerEntries,
                    withdrawResult.WithdrawalId, accountId, custodian.GetCode());
                return Ok(result);
            }

            return this.CreateAPIError(400, "withdrawals-not-supported",
                $"Withdrawals are not supported for \"{custodian.GetName()}\".");
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
