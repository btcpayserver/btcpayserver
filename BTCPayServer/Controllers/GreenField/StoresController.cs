using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
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
    public class GreenFieldController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public GreenFieldController(StoreRepository storeRepository, UserManager<ApplicationUser> userManager)
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
        
        [HttpPost("~/api/v1/stores")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateStore(CreateStoreRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var store = new Data.StoreData();
            ToModel(request, store);
            await _storeRepository.CreateStore(_userManager.GetUserId(User), store);
            return Ok(FromModel(store));
        }
        
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId, UpdateStoreRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            ToModel(request, store);
            await _storeRepository.UpdateStore(store);
            return Ok(FromModel(store));
        }

        private static Client.Models.StoreData FromModel(Data.StoreData data)
        {
            return new Client.Models.StoreData()
            {
                Id = data.Id,
                Name = data.StoreName
            };
        }
        
        private static void ToModel(StoreBaseData restModel,Data.StoreData model)
        {
            model.StoreName = restModel.Name;
        }

        private IActionResult Validate(StoreBaseData request)
        {
            if (request?.Name is null)
                ModelState.AddModelError(nameof(request.Name), "Name is missing");
            return !ModelState.IsValid ? BadRequest(new ValidationProblemDetails(ModelState)) : null;
        }
    }
}
