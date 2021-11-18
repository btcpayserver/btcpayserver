using System.Linq;
using BTCPayServer.Plugins.LNbank.Authentication;
using BTCPayServer.Plugins.LNbank.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Plugins.LNbank.Controllers.API
{
    [ApiController]
    [Route("~/plugins/lnbank/api/[controller]")]
    [Authorize(AuthenticationSchemes=AuthenticationSchemes.Api)]
    public abstract class BaseApiController : Controller
    {
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;

        protected BaseApiController(IOptionsMonitor<IdentityOptions> identityOptions)
        {
            _identityOptions = identityOptions;
        }
        
        protected string UserId => User?.Claims.First(c => c.Type == _identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType).Value;
        protected string WalletId => User?.Claims.First(c => c.Type == "WalletId").Value;
        protected Wallet Wallet => (Wallet)ControllerContext.HttpContext.Items.TryGet("Wallet");
    }
}
