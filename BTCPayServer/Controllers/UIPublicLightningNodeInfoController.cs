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
    public class UIPublicLightningNodeInfoController : Controller
    {
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly Dictionary<PaymentMethodId, IPaymentModelExtension> _paymentModelExtensions;
        private readonly UriResolver _uriResolver;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly StoreRepository _StoreRepository;

        public UIPublicLightningNodeInfoController(BTCPayNetworkProvider btcPayNetworkProvider,
            Dictionary<PaymentMethodId, IPaymentModelExtension> paymentModelExtensions,
            UriResolver uriResolver,
            PaymentMethodHandlerDictionary handlers,
            StoreRepository storeRepository)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _paymentModelExtensions = paymentModelExtensions;
            _uriResolver = uriResolver;
            _handlers = handlers;
            _StoreRepository = storeRepository;
        }

        [HttpGet]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ShowLightningNodeInfo(string storeId, string cryptoCode)
        {
            var store = await _StoreRepository.FindStore(storeId);
            var pmi = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
            if (store == null || _handlers.TryGet(pmi) is not LightningLikePaymentHandler handler)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new ShowLightningNodeInfoViewModel
            {
                CryptoCode = cryptoCode,
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob)
            };
            try
            {
                var paymentMethodDetails = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, _handlers);
                var nodeInfo = await handler.GetNodeInfo(paymentMethodDetails, null, throws: true);

                vm.Available = true;
                vm.CryptoImage = GetImage(pmi);
                vm.NodeInfo = nodeInfo.Select(n => new ShowLightningNodeInfoViewModel.NodeData(n)).ToArray();
            }
            catch (Exception)
            {
                // ignored
            }

            return View(vm);
        }

        private string GetImage(PaymentMethodId paymentMethodId)
        {
            if (_paymentModelExtensions.TryGetValue(paymentMethodId, out var paymentModelExtension))
            {
                return "/" + Url.Content(paymentModelExtension.Image);
            }
            return null;
        }
    }

    public class ShowLightningNodeInfoViewModel
    {
        public class NodeData
        {
            string _connection;
            public NodeData(NodeInfo nodeInfo)
            {
                _connection = nodeInfo.ToString();
                Id = $"{nodeInfo.Host}-{nodeInfo.Port}".Replace(".", "-", StringComparison.OrdinalIgnoreCase);
                IsTor = nodeInfo.IsTor;
            }
            public string Id { get; }
            public bool IsTor { get; }
            public override string ToString()
            {
                return _connection;
            }
        }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public NodeData[] NodeInfo { get; set; }
        public bool Available { get; set; }
        public string CryptoCode { get; set; }
        public string CryptoImage { get; set; }
        public string StoreName { get; set; }
    }
}
