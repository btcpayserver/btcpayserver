using System;
using System.Net.Mail;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        
        [Route("{storeId}/emails")]
        public IActionResult Emails()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var data = store.GetStoreBlob().EmailSettings ?? new EmailSettings();
            return View(new EmailsViewModel() { Settings = data });
        }
        
        [Route("{storeId}/emails")]
        [HttpPost]
        public async Task<IActionResult> Emails(string storeId, EmailsViewModel model, string command)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            if (command == "Test")
            {
                try
                {
                    if (!model.Settings.IsComplete())
                    {
                        TempData[WellKnownTempData.ErrorMessage] = "Required fields missing";
                        return View(model);
                    }
                    var client = model.Settings.CreateSmtpClient();
                    var message = model.Settings.CreateMailMessage(new MailAddress(model.TestEmail), "BTCPay test", "BTCPay test");
                    await client.SendMailAsync(message);
                    TempData[WellKnownTempData.SuccessMessage] = "Email sent to " + model.TestEmail + ", please, verify you received it";
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Error: " + ex.Message;
                }
                return View(model);
            }
            else // if(command == "Save")
            {
                
                var storeBlob = store.GetStoreBlob();
                storeBlob.EmailSettings = model.Settings;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings modified";
                return RedirectToAction(nameof(UpdateStore), new {
                    storeId});

            }
        }
    }
}
