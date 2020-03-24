using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Client;

namespace BTCPayServer.Controllers.RestApi
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoresController : ControllerBase
    {
        public StoresController()
        {
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores")]
        public ActionResult<IEnumerable<Client.Models.StoreData>> GetStores()
        {
           var stores = this.HttpContext.GetStoresData();
           return Ok(stores.Select(FromModel));
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
