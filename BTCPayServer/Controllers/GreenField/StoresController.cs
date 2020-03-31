using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoresController : ControllerBase
    {
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
