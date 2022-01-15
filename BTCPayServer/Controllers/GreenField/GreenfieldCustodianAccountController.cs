using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Custodian;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
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

        [HttpGet("~/api/v1/custodian-account")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ListCustodianAccount()
        {
            // var data = await _apiKeyRepository.GetKey(apiKey);
            // return Ok(FromModel(data));
            // TODO implement
            return BadRequest();
        }

        [HttpPost("~/api/v1/custodian-account")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCustodianAccount(CreateCustodianAccountRequest request)
        {
            request ??= new CreateCustodianAccountRequest();
            
            // TODO validate input
            
            var custodianAccount = new CustodianAccountData()
            {
                CustodianCode = request.CustodianCode,
                StoreId = request.StoreId,
                
            };
            var newBlob = new CustodianAccountData.CustodianAccountBlob();
            newBlob.config = request.Config;
            custodianAccount.SetBlob(newBlob);
            
            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            return Ok(custodianAccount);
        }

         
        [HttpDelete("~/api/v1/custodian-account/{id}", Order = 1)]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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

        // private static CustodianAccountData FromModel(CustodianAccountData data)
        // {
        //     return new CustodianAccountData()
        //     {
        //         Permissions = Permission.ToPermissions(data.GetBlob().Permissions).ToArray(),
        //         ApiKey = data.Id,
        //         Label = data.Label ?? string.Empty
        //     };
        // }
    }
}
