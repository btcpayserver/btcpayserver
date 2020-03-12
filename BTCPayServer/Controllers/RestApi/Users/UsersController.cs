using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Hosting.OpenApi;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace BTCPayServer.Controllers.RestApi.Users
{
    [ApiController]
    [IncludeInOpenApiDocs]
    [OpenApiTags("Users")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }
    
        [OpenApiOperation("Get current user information", "View information about the current user")]
        [SwaggerResponse(StatusCodes.Status200OK, typeof(ApiKeyData),
            Description = "Information about the current user")]
        [Authorize(Policy = Policies.CanModifyProfile.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return FromModel(user);
        }
        
        private static ApplicationUserData FromModel(ApplicationUser data)
        {
            return new ApplicationUserData()
            {
                Id = data.Id,
                Email = data.Email
            };
        }
    }
}
