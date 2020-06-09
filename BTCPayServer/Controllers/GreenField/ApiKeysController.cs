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
using NBitcoin.DataEncoders;
using NBitcoin;

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

        [HttpPost("~/api/v1/api-keys")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<ActionResult<ApiKeyData>> CreateKey(CreateApiKeyRequest request)
        {
            if (request is null)
                return NotFound();
            var key = new APIKeyData()
            {
                Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
                Type = APIKeyType.Permanent,
                UserId = _userManager.GetUserId(User),
                Label = request.Label
            };
            key.SetBlob(new APIKeyBlob()
            {
                Permissions = request.Permissions.Select(p => p.ToString()).Distinct().ToArray()
            });
            await _apiKeyRepository.CreateKey(key);
            return Ok(FromModel(key));
        }

        [HttpDelete("~/api/v1/api-keys/current")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
        public Task<IActionResult> RevokeCurrentKey()
        {
            if (!ControllerContext.HttpContext.GetAPIKey(out var apiKey))
            {
                // Should be impossible (we force apikey auth)
                return Task.FromResult<IActionResult>(BadRequest());
            }
            return RevokeKey(apiKey);
        }
        [HttpDelete("~/api/v1/api-keys/{apikey}", Order = 1)]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> RevokeKey(string apikey)
        {
            if (string.IsNullOrEmpty(apikey))
                return NotFound();
            if (await _apiKeyRepository.Remove(apikey, _userManager.GetUserId(User)))
                return Ok();
            else
                return NotFound();
        }

        private static ApiKeyData FromModel(APIKeyData data)
        {
            return new ApiKeyData()
            {
                Permissions = Permission.ToPermissions(data.GetBlob().Permissions).ToArray(),
                ApiKey = data.Id,
                Label = data.Label ?? string.Empty
            };
        }
    }
}
