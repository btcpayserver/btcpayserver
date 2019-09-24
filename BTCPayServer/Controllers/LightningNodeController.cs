using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;

namespace BTCPayServer.Controllers
{
    [Route("lightning-node")]
    public class LightningNodeController : Controller
    {
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly LightningClientFactoryService _LightningClientFactory;
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly LightningLikePaymentHandler _LightningLikePaymentHandler;
        private readonly StoreRepository _StoreRepository;

        public LightningNodeController(BTCPayNetworkProvider btcPayNetworkProvider, 
            LightningClientFactoryService lightningClientFactory,
            UserManager<ApplicationUser> userManager,
            LightningLikePaymentHandler lightningLikePaymentHandler, 
            StoreRepository storeRepository)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _LightningClientFactory = lightningClientFactory;
            _UserManager = userManager;
            _LightningLikePaymentHandler = lightningLikePaymentHandler;
            _StoreRepository = storeRepository;
        }

        [HttpGet("{cryptoCode}/channel-request")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        public async Task<IActionResult> RequestLightningChannel(string storeId, string cryptoCode, string nodeInfo, long sats, string callback, string description, string serviceName )
        {
            if (_BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode) == null)
            {
                return BadRequest("invalid crypto code");
            }
            if (!NodeInfo.TryParse(nodeInfo, out _))
            {
                return BadRequest("invalid node specified");
            }

            var stores = await _StoreRepository.GetStoresByUserId(_UserManager.GetUserId(User));

            return View(new RequestLightningChannelViewModel()
            {
                Description = description,
                Satoshis = sats,
                StoreId = storeId,
                ServiceName = serviceName,
                TargetNodeInfo = nodeInfo,
                Callback = callback,
                Stores = new SelectList(
                    stores.Select(data =>
                        new SelectListItem(data.StoreName, data.Id, data.Id == storeId)), nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text), storeId)
            });

        }

        [HttpPost("{cryptoCode}/channel-request")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]

        public async Task<IActionResult> RequestLightningChannel(string cryptoCode, RequestLightningChannelViewModel viewModel)
        {

            if (!ModelState.IsValid)
            {
                
                var stores = await _StoreRepository.GetStoresByUserId(_UserManager.GetUserId(User));
                viewModel.Stores = new SelectList(
                    stores.Select(data =>
                        new SelectListItem(data.StoreName, data.Id, data.Id == viewModel.StoreId)),
                    nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text), viewModel.StoreId);
                return View(viewModel);
            }
            
            if (_BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode) == null)
            {
                return BadRequest("invalid crypto code");
            }
            if (!NodeInfo.TryParse(viewModel.TargetNodeInfo, out var targetNodeInfo))
            {
                return BadRequest("invalid node specified");
            }

            if (!(await _StoreRepository.GetStoreUsers(viewModel.StoreId)).Any(user =>
                user.Id == _UserManager.GetUserId(User) && user.Role == StoreRoles.Owner))
            {
                return BadRequest("invalid permissions!");
            }
            
            var store = await _StoreRepository.FindStore(viewModel.StoreId);
            if (store == null)
                return NotFound();

            
            try
            {
                var paymentMethodDetails = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
//                if (store.GetStoreBlob().IsExcluded(paymentMethodDetails.PaymentId))
//                {
//                    return BadRequest("ln node unavailable");
//                }
                var network = _BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
                var ourNodeInfo =
                    await _LightningLikePaymentHandler.GetNodeInfo(Request.IsOnion() && targetNodeInfo.IsTor, paymentMethodDetails,
                        network);
                
                var lightningClient = _LightningClientFactory.Create(paymentMethodDetails.GetLightningUrl(), network);

                await lightningClient.ConnectTo(targetNodeInfo);
                var result = await lightningClient.OpenChannel(new OpenChannelRequest()
                {
                    NodeInfo = targetNodeInfo, FeeRate = FeeRate.Zero, ChannelAmount = new Money(viewModel.Satoshis)
                });

                return Redirect()
            }
            catch (Exception)
            {
                return BadRequest("ln node unavailable");
            }
        }
        
        

        public class RequestLightningChannelViewModel
        {
            [Required]
            public string TargetNodeInfo { get; set; }
            [Required]
            public long Satoshis { get; set; }
            public string Description { get; set; }
            public string ServiceName { get; set; }
            public string Callback { get; set; }
            [Required]
            public string StoreId { get; set; }
            public SelectList Stores { get; set; }
        }
        
        
        
        [HttpGet("~/embed/{storeId}/{cryptoCode}/ln")]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [AllowAnonymous]
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
