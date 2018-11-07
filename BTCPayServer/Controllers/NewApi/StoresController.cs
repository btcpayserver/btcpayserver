using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Controllers.NewApi
{
    [ApiController]
    [Route("api/v1.0/[controller]")]
    [Authorize()]
    public class StoresController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public StoresController(UserManager<ApplicationUser> userManager, StoreRepository storeRepository)
        {
            _userManager = userManager;
            _storeRepository = storeRepository;
        }

        [HttpGet("")]
        public async Task<ActionResult<IEnumerable<StoreData>>> Stores()
        {
            var stores = await _storeRepository.GetStoresByUserId(_userManager.GetUserId(User));
            return Ok(stores);
        }

        [HttpGet("{storeId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key)]
        public async Task<ActionResult<StoreData>> GetStore(string storeId)
        {
            var store = await _storeRepository.FindStore(storeId);
            return Ok(store);
        }

        [HttpPost("")]
        public async Task<ActionResult<StoreData>> CreateStore([FromBody] CreateStoreRequest request)
        {
            var store = await _storeRepository.CreateStore(_userManager.GetUserId(User), request.Name);
            return CreatedAtAction(nameof(GetStore), new {storeId = store.Id}, store);
        }

        [HttpDelete("{storeId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key)]
        public async Task<ActionResult> DeleteStore(string storeId)
        {
            var result = await _storeRepository.DeleteStore(storeId);
            if (!result)
            {
                return BadRequest();
            }

            return Ok();
        }

        [HttpPut("{storeId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key)]
        public async Task<ActionResult<StoreData>> UpdateStore(string storeId, [FromBody] StoreData store)
        {
            store.Id = storeId;
            await _storeRepository.UpdateStore(store);
            return RedirectToAction(nameof(GetStore), new {storeId});
        }
    }


    public class CreateStoreRequest
    {
        [Required] public string Name { get; set; }
    }
}
