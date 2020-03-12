using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Hosting.OpenApi;
using BTCPayServer.Security;
using BTCPayServer.Security.APIKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace BTCPayServer.Controllers.RestApi.ApiKeys
{
    [ApiController]
    [IncludeInOpenApiDocs]
    [OpenApiTags("API Keys")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
    public class ApiKeysController : ControllerBase
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public ApiKeysController(APIKeyRepository apiKeyRepository, UserManager<ApplicationUser> userManager)
        {
            _apiKeyRepository = apiKeyRepository;
            _userManager = userManager;
        }
    
        [OpenApiOperation("Get current API Key information", "View information about the current API key")]
        [SwaggerResponse(StatusCodes.Status200OK, typeof(ApiKeyData),
            Description = "Information about the current api key")]
        [HttpGet("~/api/v1/api-keys/current")]
        [HttpGet("~/api/v1/users/me/api-keys/current")]
        public async Task<ActionResult<ApiKeyData>> GetKey()
        {
            ControllerContext.HttpContext.GetAPIKey(out var apiKey);
            var data = await _apiKeyRepository.GetKey(apiKey);
            return Ok(FromModel(data));
        }
        
        [OpenApiOperation("Revoke the current API Key", "Revoke the current API key so that it cannot be used anymore")]
        [SwaggerResponse(StatusCodes.Status200OK, typeof(ApiKeyData),
            Description = "The key was revoked and is no longer usable")]
        [HttpDelete("~/api/v1/api-keys/current")]
        [HttpDelete("~/api/v1/users/me/api-keys/current")]
        public async Task<ActionResult<ApiKeyData>> RevokeKey()
        {
            ControllerContext.HttpContext.GetAPIKey(out var apiKey);
            await _apiKeyRepository.Remove(apiKey, _userManager.GetUserId(User));
            return Ok();
        }
        
        private static ApiKeyData FromModel(APIKeyData data)
        {
            return new ApiKeyData()
            {
                Permissions = data.GetPermissions(),
                ApiKey = data.Id,
                UserId = data.UserId,
                Label = data.Label
            };
        }
    }
}
