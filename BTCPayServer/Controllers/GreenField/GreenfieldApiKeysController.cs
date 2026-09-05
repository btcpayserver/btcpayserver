using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
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
    public class GreenfieldApiKeysController(APIKeyRepository apiKeyRepository, UserManager<ApplicationUser> userManager) : ControllerBase
    {
        [HttpGet("~/api/v1/api-keys/current")]
        public async Task<IActionResult> GetKey()
        {
            if (!ControllerContext.HttpContext.GetAPIKey(out var apiKey))
            {
                return
                    this.CreateAPIError(404, "api-key-not-found", "The api key was not present.");
            }
            var data = await apiKeyRepository.GetKey(new APIKeyRepository.Selector.ByApiKey(apiKey));
            data.Key = apiKey;
            return Ok(FromModel(data));
        }

        [HttpPost("~/api/v1/api-keys")]
        [Authorize(Policy = Policies.UnrestrictedUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public Task<IActionResult> CreateAPIKey(CreateApiKeyRequest request)
        => CreateUserAPIKey(User.GetId(), request);

        [HttpPost("~/api/v1/users/{idOrEmail}/api-keys")]
        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateUserAPIKey(string idOrEmail, CreateApiKeyRequest request)
        {
            request ??= new CreateApiKeyRequest();
            request.Permissions ??= System.Array.Empty<Permission>();

            var userId = (await userManager.FindByIdOrEmail(idOrEmail))?.Id;
            if (userId is null)
                return this.UserNotFound();

            var key = APIKeyRepository.New();
            key.UserId = userId;
            key.Label = request.Label;
            key.SetBlob(new APIKeyBlob()
            {
                Permissions = request.Permissions.Select(p => p.ToString()).Distinct().ToArray()
            });
            await apiKeyRepository.CreateKey(key);
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
            return RevokeAPIKey(new APIKeyRepository.Selector.ByApiKey(apiKey).GetId());
        }
        [HttpDelete("~/api/v1/api-keys/{apiKeyId}", Order = 1)]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public Task<IActionResult> RevokeAPIKey(string apiKeyId)
        => RevokeAPIKey(User.GetId(), apiKeyId);

        [HttpDelete("~/api/v1/users/{idOrEmail}/api-keys/{apiKeyId}", Order = 1)]
        [Authorize(Policy = Policies.CanManageUsers, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> RevokeAPIKey(string idOrEmail, string apiKeyId)
        {
            var userId = (await userManager.FindByIdOrEmail(idOrEmail))?.Id;
            if (userId is null)
                return this.UserNotFound();
            if (!string.IsNullOrEmpty(apiKeyId) &&
                await apiKeyRepository.Remove(new APIKeyRepository.Selector.ById(apiKeyId), userId))
                return Ok();
            else
                return this.CreateAPIError("apikey-not-found", "This apikey does not exists");
        }


        private static ApiKeyData FromModel(APIKeyData data)
        => new ApiKeyData()
        {
            Id = data.Id,
            Permissions = Permission.ToPermissions(data.GetBlob().Permissions).ToArray(),
            ApiKey = data.Key,
            Label = data.Label ?? string.Empty,
            Created = data.CreatedAt
        };
    }
}
