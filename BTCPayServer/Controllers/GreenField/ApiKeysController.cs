using System.Threading.Tasks;
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Security.GreenField;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    public class ApiKeysController : ControllerBase
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public ApiKeysController(APIKeyRepository apiKeyRepository, UserManager<ApplicationUser> userManager)
        {
            _apiKeyRepository = apiKeyRepository;
            _userManager = userManager;
        }
    
        [HttpGet("~/api/v1/api-keys/current")]
        public async Task<ActionResult<ApiKeyData>> GetKey()
        {
            if (!ControllerContext.HttpContext.GetAPIKey(out var apiKey))
            {
                return NotFound();
            }
            var data = await _apiKeyRepository.GetKey(apiKey);
            return Ok(FromModel(data));
        }

        [HttpDelete("~/api/v1/api-keys/current")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
        public async Task<ActionResult<ApiKeyData>> RevokeKey()
        {
            if (!ControllerContext.HttpContext.GetAPIKey(out var apiKey))
            {
                return NotFound();
            }
            await _apiKeyRepository.Remove(apiKey, _userManager.GetUserId(User));
            return Ok();
        }
        
        private static ApiKeyData FromModel(APIKeyData data)
        {
            return new ApiKeyData()
            {
                Permissions = Permission.ToPermissions(data.Permissions).ToArray(),
                ApiKey = data.Id,
                Label = data.Label ?? string.Empty
            };
        }
    }
}
