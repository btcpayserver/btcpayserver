using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldApiKeysController : ControllerBase
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public GreenfieldApiKeysController(APIKeyRepository apiKeyRepository, UserManager<ApplicationUser> userManager)
        {
            _apiKeyRepository = apiKeyRepository;
            _userManager = userManager;
        }

        [HttpGet("~/api/v1/api-keys/current")]
        public async Task<IActionResult> GetKey()
        {
            if (!ControllerContext.HttpContext.GetAPIKey(out var apiKey))
            {
                return
                    this.CreateAPIError(404, "api-key-not-found", "The api key was not present.");
            }
            var data = await _apiKeyRepository.GetKey(apiKey);
            return Ok(FromModel(data));
        }

        [HttpPost("~/api/v1/api-keys")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateKey(CreateApiKeyRequest request)
        {
            request ??= new CreateApiKeyRequest();
            request.Permissions ??= System.Array.Empty<Permission>();
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
            if (!string.IsNullOrEmpty(apikey) &&
                await _apiKeyRepository.Remove(apikey, _userManager.GetUserId(User)))
                return Ok();
            else
                return this.CreateAPIError("apikey-not-found", "This apikey does not exists");
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
