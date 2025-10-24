using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace BTCPayServer
{
    [Route("lnurlauth")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
    public class UILNURLAuthController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly LnurlAuthService _lnurlAuthService;
        private readonly LinkGenerator _linkGenerator;
        public IStringLocalizer StringLocalizer { get; }

        public UILNURLAuthController(UserManager<ApplicationUser> userManager, LnurlAuthService lnurlAuthService,
            IStringLocalizer stringLocalizer, LinkGenerator linkGenerator)
        {
            _userManager = userManager;
            _lnurlAuthService = lnurlAuthService;
            _linkGenerator = linkGenerator;
            StringLocalizer = stringLocalizer;
        }

        [HttpGet("{id}/delete")]
        public IActionResult Remove(string id)
        {
            return View("Confirm",
                new ConfirmModel(StringLocalizer["Remove LNURL Auth link"],
                    StringLocalizer["Your account will no longer have this Lightning wallet as an option for two-factor authentication."],
                    StringLocalizer["Delete"]));
        }

        [HttpPost("{id}/delete")]
        public async Task<IActionResult> RemoveP(string id)
        {
            await _lnurlAuthService.Remove(id, _userManager.GetUserId(User));

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = StringLocalizer["LNURL Auth was removed successfully."].Value
            });

            return RedirectToList();
        }

        [HttpGet("register")]
        public async Task<IActionResult> Create(string name)
        {
            var userId = _userManager.GetUserId(User);
            var options = await _lnurlAuthService.RequestCreation(userId);
            if (options is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = StringLocalizer["The Lightning node could not be registered."].Value
                });

                return RedirectToList();
            }

            return View(new Uri(_linkGenerator.GetUriByAction(
                action: nameof(CreateResponse),
                controller: "UILNURLAuth",
                values: new
                {
                    userId,
                    name,
                    tag = "login",
                    action = "link",
                    k1 = Encoders.Hex.EncodeData(options)
                }, Request.Scheme, Request.Host, Request.PathBase) ?? string.Empty));
        }

        [HttpGet("register/check")]
        public Task<IActionResult> CreateCheck()
        {
            var userId = _userManager.GetUserId(User);
            if (_lnurlAuthService.CreationStore.TryGetValue(userId, out _))
            {
                return Task.FromResult<IActionResult>(Ok());
            }

            return Task.FromResult<IActionResult>(NotFound());
        }

        [HttpGet("register-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateResponse(string userId, string sig, string key, string name)
        {
            if (await _lnurlAuthService.CompleteCreation(name, userId,
                ECDSASignature.FromDER(Encoders.Hex.DecodeData(sig)), new PubKey(key)))
            {
                return Ok(new LNUrlStatusResponse { Status = "OK" });
            }

            return BadRequest(new LNUrlStatusResponse
            {
                Reason = "The challenge could not be verified",
                Status = "ERROR"
            });
        }


        [HttpGet("login-check")]
        [AllowAnonymous]
        public Task<IActionResult> LoginCheck(string userId)
        {
            return _lnurlAuthService.LoginStore.ContainsKey(userId) ? Task.FromResult<IActionResult>(Ok()) : Task.FromResult<IActionResult>(NotFound());
        }

        [HttpGet("login-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginResponse(string userId, string sig, string key)
        {
            if (await _lnurlAuthService.CompleteLogin(userId,
                ECDSASignature.FromDER(Encoders.Hex.DecodeData(sig)), new PubKey(key)))
            {
                return Ok(new LNUrlStatusResponse { Status = "OK" });
            }

            return BadRequest(new LNUrlStatusResponse
            {
                Reason = "The challenge could not be verified",
                Status = "ERROR"
            });
        }

        public ActionResult RedirectToList(string successMessage = null)
        {
            if (successMessage != null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = successMessage
                });
            }

            return RedirectToAction("TwoFactorAuthentication", "UIManage");
        }
    }
}
