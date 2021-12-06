#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class StoreSendEmail : Controller
    {
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/email")]
        public async Task<IActionResult> SendEmailFromStore(string storeId,
            [FromBody] SendEmailRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            }

            var emailSettings = store.GetStoreBlob().EmailSettings ?? null;
            if (emailSettings != null || !emailSettings.IsComplete())
            {
                return this.CreateAPIError("smtp-not-configured", "Store does not have an SMTP server configured.");
            }

            var client = emailSettings.CreateSmtpClient();
            var message = emailSettings.CreateMailMessage(request.toMailAddress(), request.subject, request.body);
            try
            {
                await client.SendMailAsync(message);
                return Ok();
            }
            catch (Exception e)
            {
                return this.CreateAPIError("not-available", e.Message);
            }
        }
    }
}
