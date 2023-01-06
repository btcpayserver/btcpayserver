#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreEmailController : Controller
    {
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly StoreRepository _storeRepository;

        public GreenfieldStoreEmailController(EmailSenderFactory emailSenderFactory, StoreRepository storeRepository)
        {
            _emailSenderFactory = emailSenderFactory;
            _storeRepository = storeRepository;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/email/send")]
        public async Task<IActionResult> SendEmailFromStore(string storeId,
            [FromBody] SendEmailRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            }
            if (!MailboxAddressValidator.TryParse(request.Email, out var to))
            {
                ModelState.AddModelError(nameof(request.Email), "Invalid email");
                return this.CreateValidationError(ModelState);
            }
            var emailSender = await _emailSenderFactory.GetEmailSender(storeId);
            if (emailSender is null)
            {
                return this.CreateAPIError(404, "smtp-not-configured", "Store does not have an SMTP server configured.");
            }
            emailSender.SendEmail(to, request.Subject, request.Body);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/email")]
        public IActionResult GetStoreEmailSettings()
        {

            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/email")]
        public async Task<IActionResult> UpdateStoreEmailSettings(string storeId, EmailSettings request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }

            if (!string.IsNullOrEmpty(request.From) && !MailboxAddressValidator.IsMailboxAddress(request.From))
            {
                request.AddModelError(e => e.From,
                    "Invalid email address", this);
                return this.CreateValidationError(ModelState);
            }
            var blob = store.GetStoreBlob();
            blob.EmailSettings = request;
            if (store.SetStoreBlob(blob))
            {
                await _storeRepository.UpdateStore(store);
            }

            return Ok(FromModel(store));
        }
        private EmailSettings FromModel(Data.StoreData data)
        {
            return data.GetStoreBlob().EmailSettings ?? new();
        }
        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
