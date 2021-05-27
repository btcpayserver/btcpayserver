using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [EnableCors(CorsPolicies.All)]
    public class GreenFieldServerInfoController : Controller
    {
        private readonly BTCPayServerEnvironment _env;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly IEnumerable<ISyncSummaryProvider> _summaryProviders;

        public GreenFieldServerInfoController(
            BTCPayServerEnvironment env,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            IEnumerable<ISyncSummaryProvider>summaryProviders)
        {
            _env = env;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _summaryProviders = summaryProviders;
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/info")]
        public ActionResult ServerInfo()
        {
            var supportedPaymentMethods = _paymentMethodHandlerDictionary
                .SelectMany(handler => handler.GetSupportedPaymentMethods().Select(id => id.ToString()))
                .Distinct();
            
            ServerInfoData model = new ServerInfoData2
            {
                FullySynched = _summaryProviders.All(provider => provider.AllAvailable()),
                SyncStatus = _summaryProviders.SelectMany(provider => provider.GetStatuses()),
                Onion = _env.OnionUrl,
                Version = _env.Version,
                SupportedPaymentMethods = supportedPaymentMethods
            };
            return Ok(model);
        }
        
        public class ServerInfoData2 : ServerInfoData
        {
            public new IEnumerable<ISyncStatus> SyncStatus { get; set; }
        }
    }
}
