using System.Threading.Tasks;
using BTCPayServer.Hosting.OpenApi;
using BTCPayServer.Security;
using BTCPayServer.Security.APIKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        public ApiKeysController(APIKeyRepository apiKeyRepository)
        {
            _apiKeyRepository = apiKeyRepository;
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
            return Ok(ApiKeyData.FromModel(data));
        }
    }
}
