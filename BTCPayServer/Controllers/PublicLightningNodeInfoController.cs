using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Controllers
{
    
    [Route("embed/{storeId}/{cryptoCode}/ln")]
    [AllowAnonymous]
    public class PublicLightningNodeInfoController : Controller
    {
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly LightningLikePaymentHandler _LightningLikePaymentHandler;
        private readonly StoreRepository _StoreRepository;

        public PublicLightningNodeInfoController(BTCPayNetworkProvider btcPayNetworkProvider, 
            LightningLikePaymentHandler lightningLikePaymentHandler, StoreRepository storeRepository)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _LightningLikePaymentHandler = lightningLikePaymentHandler;
            _StoreRepository = storeRepository;
        }
        
        [HttpGet]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        public async Task<IActionResult> ShowLightningNodeInfo(string storeId, string cryptoCode)
        {
            var store = await _StoreRepository.FindStore(storeId);
            if (store == null)
                return NotFound();

            try
            {
                var paymentMethodDetails = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
                var network = _BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
                var nodeInfo =
                    await _LightningLikePaymentHandler.GetNodeInfo(this.Request.IsOnion(), paymentMethodDetails,
                        network);

                return View(new ShowLightningNodeInfoViewModel()
                {
                    Available = true,
                    NodeInfo = nodeInfo.ToString(),
                    CryptoCode = cryptoCode,
                    CryptoImage = GetImage(paymentMethodDetails.PaymentId, network)
                });
            }
            catch (Exception)
            {
                return View(new ShowLightningNodeInfoViewModel() {Available = false, CryptoCode = cryptoCode});
            }
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
        public string NodeInfo { get; set; }
        public bool Available { get; set; }
        public string CryptoCode { get; set; }
        public string CryptoImage { get; set; }
    }
}
