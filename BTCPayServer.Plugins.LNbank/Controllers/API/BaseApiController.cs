using System.Linq;
using BTCPayServer.Plugins.LNbank.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LNbank.Controllers.API
{
    [ApiController]
    [Route("~/Plugins/LNbank/api/[controller]")]
    [Authorize(AuthenticationSchemes=AuthenticationSchemes.Api)]
    public abstract class BaseApiController : Controller
    {
        protected string UserId => User?.Claims.First(c => c.Type == "UserId").Value;
    }
}
