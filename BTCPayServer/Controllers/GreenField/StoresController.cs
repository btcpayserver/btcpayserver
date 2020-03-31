using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoresController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoresController(StoreRepository storeRepository, UserManager<ApplicationUser> userManager)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
        }
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores")]
        public ActionResult<IEnumerable<Client.Models.StoreData>> GetStores()
        {
           var stores = HttpContext.GetStoresData();
           return Ok(stores.Select(FromModel));
        }
        
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}")]
        public ActionResult<Client.Models.StoreData> GetStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }
            return Ok(FromModel(store));
        }
        
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}")]
        public async Task<ActionResult> RemoveStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            if (!_storeRepository.CanDeleteStores())
            {
                ModelState.AddModelError(string.Empty, "BTCPay Server is using a database server that does not allow you to remove stores.");
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
            await _storeRepository.RemoveStore(storeId, _userManager.GetUserId(User));
            return Ok();
        }
        
        private static Client.Models.StoreData FromModel(Data.StoreData data)
        {
            return new Client.Models.StoreData()
            {
                Id = data.Id,
                Name = data.StoreName
            };
        }
    }
}
