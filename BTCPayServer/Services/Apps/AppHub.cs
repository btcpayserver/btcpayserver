using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Controllers;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.Crowdfund.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.Services.Apps
{
    public class AppHub : Hub
    {
        public const string InvoiceCreated = "InvoiceCreated";
        public const string PaymentReceived = "PaymentReceived";
        public const string InfoUpdated = "InfoUpdated";
        public const string InvoiceError = "InvoiceError";
        
        private readonly UICrowdfundController _crowdfundController;

        public AppHub(UICrowdfundController crowdfundController)
        {
            _crowdfundController = crowdfundController;
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
            _crowdfundController.ControllerContext.HttpContext = Context.GetHttpContext();
            try
            {

                var result =
                    await _crowdfundController.ContributeToCrowdfund(Context.Items["app"].ToString(), model, Context.ConnectionAborted);
                switch (result)
                {
                    case OkObjectResult okObjectResult:
                        await Clients.Caller.SendCoreAsync(InvoiceCreated, new[] { okObjectResult.Value.ToString() });
                        break;
                    case ObjectResult objectResult:
                        await Clients.Caller.SendCoreAsync(InvoiceError, new[] { objectResult.Value });
                        break;
                    default:
                        await Clients.Caller.SendCoreAsync(InvoiceError, Array.Empty<object>());
                        break;
                }
            }
            catch (Exception)
            {
                await Clients.Caller.SendCoreAsync(InvoiceError, Array.Empty<object>());
            }
        }

        public static string GetHubPath(HttpRequest request)
        {
            return request.GetRelativePathOrAbsolute("/apps/hub");
        }

        public static void Register(IEndpointRouteBuilder route)
        {
            route.MapHub<AppHub>("/apps/hub");
        }
    }
}
