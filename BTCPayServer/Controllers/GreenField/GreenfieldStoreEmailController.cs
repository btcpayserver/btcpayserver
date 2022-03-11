#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreEmailController : Controller
    {
        private readonly EmailSenderFactory _emailSenderFactory;

        public GreenfieldStoreEmailController(EmailSenderFactory  emailSenderFactory)
        {
            _emailSenderFactory = emailSenderFactory;
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
            var emailSender = await _emailSenderFactory.GetEmailSender(storeId);
            if (emailSender is null )
            {
                return this.CreateAPIError(404,"smtp-not-configured", "Store does not have an SMTP server configured.");
            }
            
            emailSender.SendEmail(request.Email, request.Subject, request.Body);
            return Ok();
        }
    }
}
