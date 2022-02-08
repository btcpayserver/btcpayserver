using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian;
using BTCPayServer.Services.Custodian.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using CustodianAccountData = BTCPayServer.Data.CustodianAccountData;

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
                var custodianAccount = ToModel(custodianAccountData);

                if (assetBalances)
                {
                    var custodianCode = custodianAccount.CustodianCode;
                    var custodian = _custodianRegistry.getAll()[custodianCode];
                    var balances = await custodian.GetAssetBalances(custodianAccount.Config);
                    custodianAccount.AssetBalances = balances;
                }

                responses[i] = custodianAccount;
            }

            //.Select(ToModel).ToList();


            return Ok(r);
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

            var custodianAccount = ToModel(custodianAccountData);
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

        private CustodianAccountResponse ToModel(CustodianAccountData custodianAccount)
        {
            var r = new CustodianAccountResponse();
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
            return Ok(custodianAccount);
        }


        [HttpDelete("~/api/v1/custodian-account/{id}", Order = 1)]
        [Authorize(Policy = Policies.CanModifyCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> DeleteCustodianAccount(string id)
        {
            //TODO implement
            return BadRequest();
            // if (!string.IsNullOrEmpty(id) && await _custodianAccountRepository.Remove(id, _userManager.GetUserId(User)))
            // {
            //     return Ok();
            // }
            // return this.CreateAPIError("custodian-account-not-found", "This custodian account does not exist");
        }

        [HttpGet("~/api/v1/store/{storeId}/custodian-account/{accountId}/{paymentMethod}/address")]
        [Authorize(Policy = Policies.CanDepositToCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetDepositAddress(string storeId, string accountId, string paymentMethod,
            CreateCustodianAccountRequest request)
        {
            var custodianAccount = _custodianAccountRepository.FindById(accountId);
            var custodian = _custodianRegistry.getAll()[custodianAccount.Result.CustodianCode];

            if (custodian is ICanDeposit depositableCustodian)
            {
                var result = depositableCustodian.GetDepositAddress(paymentMethod);
                return Ok(result);
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
                var result = tradableCustodian.TradeMarket(request.fromAsset, request.toAsset, request.qty, custodianAccount.GetBlob().config);
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

        // TODO withdraw endpoint
    }

    public class TradeRequestData
    {
        public string fromAsset { set; get; }
        public string toAsset { set; get; }
        public decimal qty { set; get; }
    }
}
