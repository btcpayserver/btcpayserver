using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Models;
using ExchangeSharp;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.U2F.Models
{

    [Route("fido2")]
    public class Fido2Controller : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Fido2Service _fido2Service;

        public Fido2Controller(UserManager<ApplicationUser> userManager,  Fido2Service fido2Service)
        {
            _userManager = userManager;
            _fido2Service = fido2Service;
        }

        [HttpGet("")]
        [Authorize]
        public async Task<IActionResult> List()
        {
            return View(new Fido2AuthenticationViewModel()
            {
                Credentials = await _fido2Service.GetCredentials( _userManager.GetUserId(User))
            });
        }

        [HttpGet("{id}/delete")]
        [Authorize]
        public IActionResult Remove(string id)
        { 
            return View("Confirm", new ConfirmModel("Are you sure you want to remove FIDO2 credential?", "Your account will no longer have this credential as an option for MFA.", "Remove"));
        }

        [HttpPost("{id}/delete")]
        [Authorize]
        public async Task<IActionResult> RemoveP(string id)
        {
           
            await _fido2Service.Remove(id, _userManager.GetUserId(User));
           
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = $"FIDO2 Credentials were removed successfully."
            });
            
            return RedirectToAction(nameof(List));
        }

        [HttpGet("register")]
        [Authorize]
        public async Task<IActionResult> Create(AddFido2CredentialViewModel viewModel)
        {
            var options = await _fido2Service.RequestCreation(_userManager.GetUserId(User));
            if (options is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"FIDO2 Credentials could not be saved."
                });
                
                return RedirectToAction(nameof(List));
            }

            ViewData["CredentialName"] = viewModel.Name ?? "";
            return View(options);
        }

        [HttpPost("register")]
        [Authorize]
        public async Task<IActionResult> CreateResponse([FromForm] string data, [FromForm] string name)
        {
            var attestationResponse = JObject.Parse(data).ToObject<AuthenticatorAttestationRawResponse>();
            if (await _fido2Service.CompleteCreation(_userManager.GetUserId(User), name, attestationResponse))
            {

                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = $"FIDO2 Credentials were saved successfully."
                });
            }
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"FIDO2 Credentials could not be saved."
                });
            }

            return RedirectToAction(nameof(List));
        }

    }
}
