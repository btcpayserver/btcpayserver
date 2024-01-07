using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
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
        private readonly LightningLikePaymentHandler _LightningLikePaymentHandler;
        private readonly StoreRepository _StoreRepository;

        public UIPublicLightningNodeInfoController(BTCPayNetworkProvider btcPayNetworkProvider,
            LightningLikePaymentHandler lightningLikePaymentHandler, StoreRepository storeRepository)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _LightningLikePaymentHandler = lightningLikePaymentHandler;
            _StoreRepository = storeRepository;
        }

        [HttpGet]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ShowLightningNodeInfo(string storeId, string cryptoCode)
        {
            var store = await _StoreRepository.FindStore(storeId);
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new ShowLightningNodeInfoViewModel
            {
                CryptoCode = cryptoCode,
                StoreName = store.StoreName,
                StoreBranding = new StoreBrandingViewModel(storeBlob)
            };
            try
            {
                var paymentMethodDetails = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
                var network = _BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
                var nodeInfo = await _LightningLikePaymentHandler.GetNodeInfo(paymentMethodDetails, network,
                    new InvoiceLogs(), throws: true);

                vm.Available = true;
                vm.CryptoImage = GetImage(paymentMethodDetails.PaymentId, network);
                vm.NodeInfo = nodeInfo.Select(n => new ShowLightningNodeInfoViewModel.NodeData(n)).ToArray();
            }
            catch (Exception)
            {
                // ignored
            }

            return View(vm);
        }

        private LightningSupportedPaymentMethod GetExistingLightningSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var existing = store.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        private string GetImage(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            var res = paymentMethodId.PaymentType == PaymentTypes.BTCLike
                ? Url.Content(network.CryptoImagePath)
                : Url.Content(network.LightningImagePath);
            return "/" + res;
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
