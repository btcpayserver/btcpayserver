using System;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models.AppViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Hubs
{
    public class CrowdfundHub: Hub
    {
        public const string InvoiceCreated = "InvoiceCreated";
        public const string PaymentReceived = "PaymentReceived";
        public const string InfoUpdated = "InfoUpdated";
        public const string InvoiceError = "InvoiceError";
        private readonly AppsPublicController _AppsPublicController;

        public CrowdfundHub(AppsPublicController appsPublicController)
        {
            _AppsPublicController = appsPublicController;
        }
        public async Task ListenToCrowdfundApp(string appId)
        {
            if (Context.Items.ContainsKey("app"))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.Items["app"].ToString());
                Context.Items.Remove("app");
            }
            Context.Items.Add("app", appId);
            await Groups.AddToGroupAsync(Context.ConnectionId, appId);
        }


        public async Task CreateInvoice(ContributeToCrowdfund model)
        {
               model.RedirectToCheckout = false;
               _AppsPublicController.ControllerContext.HttpContext = Context.GetHttpContext();
               try
               {

                   var result =
                       await _AppsPublicController.ContributeToCrowdfund(Context.Items["app"].ToString(), model);
                   switch (result)
                   {
                       case OkObjectResult okObjectResult:
                           await Clients.Caller.SendCoreAsync(InvoiceCreated, new[] {okObjectResult.Value.ToString()});
                           break;
                       case ObjectResult objectResult:
                           await Clients.Caller.SendCoreAsync(InvoiceError, new[] {objectResult.Value});
                           break;
                       default:
                           await Clients.Caller.SendCoreAsync(InvoiceError, System.Array.Empty<object>());
                           break;
                   }
               }
               catch (Exception)
               {
                   await Clients.Caller.SendCoreAsync(InvoiceError, System.Array.Empty<object>());

               }

        }

    }
}
