using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("embed/{storeId}/{cryptoCode}/ln")]
    [AllowAnonymous]
    public class UIPublicLightningNodeInfoController(
        Dictionary<PaymentMethodId, ICheckoutModelExtension> paymentModelExtensions,
        UriResolver uriResolver,
        BTCPayServerEnvironment env,
        PaymentMethodHandlerDictionary handlers,
        StoreRepository storeRepository)
        : Controller
    {
        [HttpGet]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ShowLightningNodeInfo(string storeId, string cryptoCode, bool showLocal = false)
        {
            showLocal &= env.CheatMode;
            var store = await storeRepository.FindStore(storeId);
            var pmi = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
            if (store == null || handlers.TryGet(pmi) is not LightningLikePaymentHandler handler)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new ShowLightningNodeInfoViewModel
            {
                CryptoCode = cryptoCode,
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeBlob)
            };
            try
            {
                var paymentMethodDetails = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, handlers);
                if (paymentMethodDetails is null)
                    return NotFound();
                var nodeInfo = await handler.GetNodeInfo(paymentMethodDetails, null, throws: true);
                vm.Available = true;
                vm.CryptoImage = GetImage(pmi);
                vm.NodeInfo = nodeInfo.Select(n => new ShowLightningNodeInfoViewModel.NodeData(n)).Where(n => showLocal || !n.IsLocal).ToArray();
            }
            catch (Exception)
            {
                // ignored
            }

            return View(vm);
        }

        private string GetImage(PaymentMethodId paymentMethodId)
        {
            if (paymentModelExtensions.TryGetValue(paymentMethodId, out var paymentModelExtension))
            {
                return "/" + Url.Content(paymentModelExtension.Image);
            }
            return null;
        }
    }

    public class ShowLightningNodeInfoViewModel
    {
        public class NodeData(NodeInfo nodeInfo)
        {
            private readonly string _connection = nodeInfo.ToString();

            public bool IsLocal { get; set; } = Extensions.IsLocalNetwork(nodeInfo.Host);

            public string Id { get; } = $"{nodeInfo.Host}-{nodeInfo.Port}".Replace(".", "-", StringComparison.OrdinalIgnoreCase);
            public bool IsTor { get; } = nodeInfo.IsTor;

            public override string ToString() => _connection;
        }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public NodeData[] NodeInfo { get; set; }
        public bool Available { get; set; }
        public string CryptoCode { get; set; }
        public string CryptoImage { get; set; }
        public string StoreName { get; set; }
    }
}
