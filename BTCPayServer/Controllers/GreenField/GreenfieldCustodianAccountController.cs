using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Custodians.Client;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Custodian;
using BTCPayServer.Services.Custodian.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using CustodianAccountData = BTCPayServer.Data.CustodianAccountData;
using CustodianAccountDataClient = BTCPayServer.Client.Models.CustodianAccountData;

namespace BTCPayServer.Controllers.Greenfield
{
    public class CustodianExceptionFilter : Attribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is CustodianApiException ex)
            {
                context.Result = new ObjectResult(new GreenfieldAPIError(ex.Code, ex.Message)) { StatusCode = ex.HttpStatus };
                context.ExceptionHandled = true;
            }
        }
    }

    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    [EnableCors(CorsPolicies.All)]
    [CustodianExceptionFilter]
    [ExperimentalRouteAttribute] // if you remove this, also remove "x_experimental": true in swagger.template.custodians.json
    public class GreenfieldCustodianAccountController : ControllerBase
    {
        private readonly CustodianAccountRepository _custodianAccountRepository;
        private readonly IEnumerable<ICustodian> _custodianRegistry;
        private readonly IAuthorizationService _authorizationService;

        public GreenfieldCustodianAccountController(CustodianAccountRepository custodianAccountRepository,
            IEnumerable<ICustodian> custodianRegistry,
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
            var custodianAccounts = await _custodianAccountRepository.FindByStoreId(storeId);

            CustodianAccountDataClient[] responses = new CustodianAccountDataClient[custodianAccounts.Length];

            for (int i = 0; i < custodianAccounts.Length; i++)
            {
                var custodianAccountData = custodianAccounts[i];
                responses[i] = await ToModel(custodianAccountData, assetBalances, cancellationToken);
            }

            return Ok(responses);
        }


        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}")]
        [Authorize(Policy = Policies.CanViewCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId,
            [FromQuery] bool assetBalances = false, CancellationToken cancellationToken = default)
        {
            var custodianAccountData = await GetCustodianAccount(storeId, accountId);
            if (custodianAccountData == null)
            {
                return this.CreateAPIError(404, "custodian-account-not-found", "The custodian account was not found.");
            }
            var custodianAccount = await ToModel(custodianAccountData, assetBalances, cancellationToken);
            return Ok(custodianAccount);
        }

        // [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/config")]
        // [Authorize(Policy = Policies.CanManageCustodianAccounts,
        //     AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        // public async Task<IActionResult> FetchCustodianAccountConfigForm(string storeId, string accountId,
        //     [FromQuery] string locale = "en-US", CancellationToken cancellationToken = default)
        // {
        //     // TODO this endpoint needs tests
        //     var custodianAccountData = await GetCustodianAccount(storeId, accountId);
        //     var custodianAccount = await ToModel(custodianAccountData, false, cancellationToken);
        //     
        //     var custodian = GetCustodianByCode(custodianAccount.CustodianCode);
        //     var form = await custodian.GetConfigForm(custodianAccount.Config, locale, cancellationToken);
        //         
        //     return Ok(form);
        // }
        //
        // [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/config")]
        // [Authorize(Policy = Policies.CanManageCustodianAccounts,
        //     AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        // public async Task<IActionResult> PostCustodianAccountConfigForm(string storeId, string accountId, JObject values,
        //     [FromQuery] string locale = "en-US", CancellationToken cancellationToken = default)
        // {
        //     // TODO this endpoint needs tests
        //     var custodianAccountData = await GetCustodianAccount(storeId, accountId);
        //     var custodianAccount = await ToModel(custodianAccountData, false, cancellationToken);
        //     
        //     var custodian = GetCustodianByCode(custodianAccount.CustodianCode);
        //     var form = await custodian.GetConfigForm(values, locale, cancellationToken);
        //     
        //     if (form.IsValid())
        //     {
        //         // TODO save the data to the config so it is persisted
        //     }
        //     
        //     return Ok(form);
        // }

        private async Task<bool> CanSeeCustodianAccountConfig()
        {
            return (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanManageCustodianAccounts))).Succeeded;
        }

        private async Task<CustodianAccountDataClient> ToModel(CustodianAccountData custodianAccount, bool includeAsset, CancellationToken cancellationToken)
        {
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);
            var r = includeAsset ? new CustodianAccountResponse() : new CustodianAccountDataClient();
            r.Id = custodianAccount.Id;
            r.CustodianCode = custodian.Code;
            r.Name = custodianAccount.Name;
            r.StoreId = custodianAccount.StoreId;
            if (await CanSeeCustodianAccountConfig())
            {
                // Only show the "config" field if the user can create or manage the Custodian Account, because config contains sensitive information (API key, etc).
                r.Config = custodianAccount.GetBlob();
            }
            if (includeAsset)
            {
                var balances = await GetCustodianByCode(r.CustodianCode).GetAssetBalancesAsync(r.Config, cancellationToken);
                ((CustodianAccountResponse)r).AssetBalances = balances;
            }
            return r;
        }

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts")]
        [Authorize(Policy = Policies.CanManageCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountRequest request, CancellationToken cancellationToken)
        {
            request ??= new CreateCustodianAccountRequest();
            var custodian = GetCustodianByCode(request.CustodianCode);

            // Use the name provided or if none provided use the name of the custodian.
            string name = string.IsNullOrEmpty(request.Name) ? custodian.Name : request.Name;

            var custodianAccount = new CustodianAccountData() { CustodianCode = custodian.Code, Name = name, StoreId = storeId, };
            custodianAccount.SetBlob(request.Config);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(await ToModel(custodianAccount, false, cancellationToken));
        }


        [HttpPut("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}")]
        [Authorize(Policy = Policies.CanManageCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateCustodianAccount(string storeId, string accountId,
            CreateCustodianAccountRequest request, CancellationToken cancellationToken = default)
        {
            request ??= new CreateCustodianAccountRequest();

            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(request.CustodianCode);

            // TODO If storeId is not valid, we get a foreign key SQL error. Is this okay or do we want to check the storeId first?
            custodianAccount.CustodianCode = custodian.Code;
            custodianAccount.StoreId = storeId;
            custodianAccount.Name = request.Name;

            custodianAccount.SetBlob(request.Config);

            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(await ToModel(custodianAccount, false, cancellationToken));
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

            throw CustodianAccountNotFound();
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/addresses/{paymentMethod}")]
        [Authorize(Policy = Policies.CanDepositToCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetDepositAddress(string storeId, string accountId, string paymentMethod, CancellationToken cancellationToken = default)
        {
            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);
            var config = custodianAccount.GetBlob();

            if (custodian is ICanDeposit depositableCustodian)
            {
                var pm = PaymentMethodId.TryParse(paymentMethod);
                if (pm == null)
                {
                    return this.CreateAPIError(400, "unsupported-payment-method",
                        $"Unsupported payment method.");
                }
                var result = await depositableCustodian.GetDepositAddressAsync(paymentMethod, config, cancellationToken);
                return Ok(result);
            }

            return this.CreateAPIError(400, "deposit-payment-method-not-supported",
                $"Deposits to \"{custodian.Name}\" are not supported using \"{paymentMethod}\".");
        }

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/market")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> MarketTradeCustodianAccountAsset(string storeId, string accountId,
            TradeRequestData request, CancellationToken cancellationToken = default)
        {
            // TODO add SATS check everywhere. We cannot change to 'BTC' ourselves because the qty / price would be different too.
            if ("SATS".Equals(request.FromAsset) || "SATS".Equals(request.ToAsset))
            {
                return this.CreateAPIError(400, "use-asset-synonym",
                    $"Please use 'BTC' instead of 'SATS'.");
            }

            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanTrade tradableCustodian)
            {
                decimal qty;
                try
                {
                    qty = await ParseQty(request.Qty, request.FromAsset, custodianAccount, custodian, cancellationToken);
                }
                catch (Exception ex)
                {
                    return UnsupportedAsset(request.FromAsset, ex.Message);
                }
                try
                {
                    var result = await tradableCustodian.TradeMarketAsync(request.FromAsset, request.ToAsset, qty,
                        custodianAccount.GetBlob(), cancellationToken);

                    return Ok(ToModel(result, accountId, custodianAccount.CustodianCode));
                }
                catch (CustodianApiException e)
                {
                    return this.CreateAPIError(e.HttpStatus, e.Code,
                        e.Message);
                }
            }

            return this.CreateAPIError(400, "market-trade-not-supported",
                $"Placing market orders on \"{custodian.Name}\" is not supported.");
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

            var custodianAccount = await GetCustodianAccount(storeId, accountId);

            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanTrade tradableCustodian)
            {
                var priceQuote = await tradableCustodian.GetQuoteForAssetAsync(fromAsset, toAsset, custodianAccount.GetBlob(), cancellationToken);
                return Ok(new TradeQuoteResponseData(priceQuote.FromAsset, priceQuote.ToAsset, priceQuote.Bid, priceQuote.Ask));
            }

            return this.CreateAPIError(400, "getting-quote-not-supported",
                $"Getting a price quote on \"{custodian.Name}\" is not supported.");
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/{tradeId}")]
        [Authorize(Policy = Policies.CanTradeCustodianAccount,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetTradeInfo(string storeId, string accountId, string tradeId, CancellationToken cancellationToken = default)
        {
            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanTrade tradableCustodian)
            {
                var result = await tradableCustodian.GetTradeInfoAsync(tradeId, custodianAccount.GetBlob(), cancellationToken);
                if (result == null)
                {
                    return this.CreateAPIError(404, "trade-not-found",
                        $"Could not find the the trade with ID {tradeId} on {custodianAccount.Name}");
                }
                return Ok(ToModel(result, accountId, custodianAccount.CustodianCode));
            }

            return this.CreateAPIError(400, "fetching-trade-info-not-supported",
                $"Fetching past trade info on \"{custodian.Name}\" is not supported.");
        }

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals/simulation")]
        [Authorize(Policy = Policies.CanWithdrawFromCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> SimulateWithdrawal(string storeId, string accountId,
            WithdrawRequestData request, CancellationToken cancellationToken = default)
        {
            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                var pm = PaymentMethodId.TryParse(request.PaymentMethod);
                if (pm == null)
                {
                    return this.CreateAPIError(400, "unsupported-payment-method",
                        $"Unsupported payment method.");
                }
                var asset = pm.CryptoCode;
                decimal qty;
                try
                {
                    qty = await ParseQty(request.Qty, asset, custodianAccount, custodian, cancellationToken);
                }
                catch (Exception ex)
                {
                    return UnsupportedAsset(asset, ex.Message);
                }

                var simulateWithdrawResult =
                    await withdrawableCustodian.SimulateWithdrawalAsync(request.PaymentMethod, qty, custodianAccount.GetBlob(), cancellationToken);
                var result = new WithdrawalSimulationResponseData(simulateWithdrawResult.PaymentMethod, simulateWithdrawResult.Asset,
                     accountId, custodian.Code, simulateWithdrawResult.LedgerEntries, simulateWithdrawResult.MinQty, simulateWithdrawResult.MaxQty);
                return Ok(result);
            }

            return this.CreateAPIError(400, "withdrawals-not-supported",
                $"Withdrawals are not supported for \"{custodian.Name}\".");
        }

        [HttpPost("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals")]
        [Authorize(Policy = Policies.CanWithdrawFromCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateWithdrawal(string storeId, string accountId,
            WithdrawRequestData request, CancellationToken cancellationToken = default)
        {
            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                var pm = PaymentMethodId.TryParse(request.PaymentMethod);
                if (pm == null)
                {
                    return this.CreateAPIError(400, "unsupported-payment-method",
                        $"Unsupported payment method.");
                }
                var asset = pm.CryptoCode;
                decimal qty;
                try
                {
                    qty = await ParseQty(request.Qty, asset, custodianAccount, custodian, cancellationToken);
                }
                catch (Exception ex)
                {
                    return UnsupportedAsset(asset, ex.Message);
                }

                var withdrawResult =
                        await withdrawableCustodian.WithdrawToStoreWalletAsync(request.PaymentMethod, qty, custodianAccount.GetBlob(), cancellationToken);
                var result = new WithdrawalResponseData(withdrawResult.PaymentMethod, withdrawResult.Asset, withdrawResult.LedgerEntries,
                    withdrawResult.WithdrawalId, accountId, custodian.Code, withdrawResult.Status, withdrawResult.CreatedTime, withdrawResult.TargetAddress, withdrawResult.TransactionId);
                return Ok(result);
            }

            return this.CreateAPIError(400, "withdrawals-not-supported",
                $"Withdrawals are not supported for \"{custodian.Name}\".");
        }

        private IActionResult UnsupportedAsset(string asset, string err)
        {
            return this.CreateAPIError(400, "invalid-qty", $"It is impossible to use % quantity with this asset ({err})");
        }

        private async Task<decimal> ParseQty(TradeQuantity qty, string asset, CustodianAccountData custodianAccount, ICustodian custodian, CancellationToken cancellationToken = default)
        {
            if (qty.Type == TradeQuantity.ValueType.Exact)
                return qty.Value;
            // Percentage of current holdings => calculate the amount
            var config = custodianAccount.GetBlob();
            var balances = await custodian.GetAssetBalancesAsync(config, cancellationToken);
            if (!balances.TryGetValue(asset, out var assetBalance))
                return 0.0m;
            return (assetBalance * qty.Value) / 100m;
        }

        async Task<CustodianAccountData> GetCustodianAccount(string storeId, string accountId)
        {
            var cust = await _custodianAccountRepository.FindById(storeId, accountId);
            if (cust is null)
                throw CustodianAccountNotFound();
            return cust;
        }

        JsonHttpException CustodianAccountNotFound()
        {
            return new JsonHttpException(this.CreateAPIError(404, "custodian-account-not-found", "Could not find the custodian account"));
        }

        ICustodian GetCustodianByCode(string custodianCode)
        {
            var cust = _custodianRegistry.FirstOrDefault(custodian => custodian.Code.Equals(custodianCode, StringComparison.OrdinalIgnoreCase));
            if (cust is null)
                throw new JsonHttpException(this.CreateAPIError(422, "custodian-code-not-found", "The custodian of this account isn't referenced in /api/v1/custodians"));
            return cust;
        }

        [HttpGet("~/api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals/{paymentMethod}/{withdrawalId}")]
        [Authorize(Policy = Policies.CanWithdrawFromCustodianAccounts,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetWithdrawalInfo(string storeId, string accountId, string paymentMethod, string withdrawalId, CancellationToken cancellationToken = default)
        {
            var custodianAccount = await GetCustodianAccount(storeId, accountId);
            var custodian = GetCustodianByCode(custodianAccount.CustodianCode);

            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                var withdrawResult = await withdrawableCustodian.GetWithdrawalInfoAsync(paymentMethod, withdrawalId, custodianAccount.GetBlob(), cancellationToken);
                if (withdrawResult == null)
                {
                    return this.CreateAPIError(404, "withdrawal-not-found", "The withdrawal was not found.");
                }
                var result = new WithdrawalResponseData(withdrawResult.PaymentMethod, withdrawResult.Asset, withdrawResult.LedgerEntries,
                    withdrawResult.WithdrawalId, accountId, custodian.Code, withdrawResult.Status, withdrawResult.CreatedTime, withdrawResult.TargetAddress, withdrawResult.TransactionId);
                return Ok(result);
            }

            return this.CreateAPIError(400, "fetching-withdrawal-info-not-supported",
                $"Fetching withdrawal information is not supported for \"{custodian.Name}\".");
        }
    }

}
