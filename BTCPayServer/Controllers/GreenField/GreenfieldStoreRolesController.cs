using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreRolesController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;

        public GreenfieldStoreRolesController(StoreRepository storeRepository)
        {
            _storeRepository = storeRepository;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/roles")]
        public async Task<IActionResult> GetStoreRoles(string storeId)
        {
            var store = HttpContext.GetStoreData();
            return store == null
                ? StoreNotFound()
                : Ok(FromModel(await _storeRepository.GetStoreRoles(storeId, false, true)));
        }


        private List<RoleData> FromModel(StoreRepository.StoreRole[] data)
        {
            return data.Select(r => new RoleData() {Role = r.Role, Id = r.Id, Policies = r.Policies}).ToList();
        }

        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
    
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldServerRolesController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;

        public GreenfieldServerRolesController(StoreRepository storeRepository)
        {
            _storeRepository = storeRepository;
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/roles")]
        public async Task<IActionResult> GetServerRoles()
        {
            return Ok(FromModel(await _storeRepository.GetStoreRoles(null, false, false)));
        }
        private List<RoleData> FromModel(StoreRepository.StoreRole[] data)
        {
            return data.Select(r => new RoleData() {Role = r.Role, Id = r.Id, Policies = r.Policies}).ToList();
        }

        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
