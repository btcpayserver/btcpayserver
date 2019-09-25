using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using DBriize.Utils;
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
        private readonly IFeeProviderFactory _FeeRateProvider;
        private readonly StoreRepository _StoreRepository;

        public LightningNodeController(BTCPayNetworkProvider btcPayNetworkProvider,
            LightningClientFactoryService lightningClientFactory,
            UserManager<ApplicationUser> userManager,
            LightningLikePaymentHandler lightningLikePaymentHandler,
            IFeeProviderFactory feeRateProvider,
            StoreRepository storeRepository)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _LightningClientFactory = lightningClientFactory;
            _UserManager = userManager;
            _LightningLikePaymentHandler = lightningLikePaymentHandler;
            _FeeRateProvider = feeRateProvider;
            _StoreRepository = storeRepository;
        }

        [HttpGet("{cryptoCode}/open-channel")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        public async Task<IActionResult> OpenLightningChannel(string storeId, string cryptoCode, string nodeInfo,
            long sats, string callback, string description, string serviceName){
        
            var network = _BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
            {
                return BadRequest("invalid crypto code");
            }
            return View(new RequestLightningChannelViewModel()
            {
                Description = description,
                Satoshis = sats,
                StoreId = storeId,
                ServiceName = serviceName,
                TargetNodeInfo = nodeInfo,
                Callback = callback,
                FeeRate = (await _FeeRateProvider.CreateFeeProvider(network).GetFeeRateAsync()).SatoshiPerByte,
                Stores = await GetStoresList(storeId)
            });
        }

        [HttpPost("{cryptoCode}/open-channel")]
        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
        public async Task<IActionResult> OpenLightningChannel(string cryptoCode,
            RequestLightningChannelViewModel viewModel)
        {
            if (!NodeInfo.TryParse(viewModel.TargetNodeInfo, out var targetNodeInfo))
            {
                ModelState.AddModelError(nameof(viewModel.TargetNodeInfo), "The node info is not in a valid format.");
            }
            if (_BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode) == null)
            {
                ModelState.AddModelError(string.Empty, "invalid crypto code specified");
            }

            if (!string.IsNullOrEmpty(viewModel.StoreId) && !(await _StoreRepository.GetStoreUsers(viewModel.StoreId)).Any(user =>
                    user.Id == _UserManager.GetUserId(User) && user.Role == StoreRoles.Owner))
            {
                ModelState.AddModelError(nameof(viewModel.StoreId), "You are not an owner of the specified store");
            }
            
            if (!ModelState.IsValid)
            {
                viewModel.Stores = await GetStoresList(viewModel.StoreId);
                return View(viewModel);
            }

            var store = await _StoreRepository.FindStore(viewModel.StoreId);
            if (store == null)
                return NotFound();

            try
            {
                var paymentMethodDetails = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
                var network = _BtcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
                var ourNodeInfo =
                    await _LightningLikePaymentHandler.GetNodeInfo(Request.IsOnion() && targetNodeInfo.IsTor,
                        paymentMethodDetails,
                        network);

                var lightningClient = _LightningClientFactory.Create(paymentMethodDetails.GetLightningUrl(), network);

                await lightningClient.ConnectTo(targetNodeInfo);
                var result = await lightningClient.OpenChannel(new OpenChannelRequest()
                {
                    NodeInfo = targetNodeInfo, FeeRate = new FeeRate(viewModel.FeeRate), ChannelAmount = new Money(viewModel.Satoshis)
                });

                if (string.IsNullOrEmpty(viewModel.Callback))
                {
                    viewModel.StatusMessage = new StatusMessageModel()
                    {
                        Severity = result.Result == OpenChannelResult.Ok
                            ? StatusMessageModel.StatusSeverity.Success
                            : StatusMessageModel.StatusSeverity.Error,
                        Message = $"Opening channel resulted with {result.Result.ToString()}"
                    }.ToString();
                    viewModel.Stores = await GetStoresList(viewModel.StoreId);
                    return View(viewModel);
                }

                return Redirect(viewModel.Callback.ReplaceMultiple(new Dictionary<string, string>()
                {
                    {"{NodeId}", ourNodeInfo.ToString()}, {"{Result}", result.ToString()}
                }));
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "The store's ln node was not available.");
                viewModel.Stores = await GetStoresList(viewModel.StoreId);
                return View(viewModel);
            }
        }

        private async Task<SelectList> GetStoresList(string selectedStoreId)
        {
            var stores = await _StoreRepository.GetStoresByUserId(_UserManager.GetUserId(User));
            return new SelectList(
                stores.Select(data =>
                    new SelectListItem(data.StoreName, data.Id, data.Id == selectedStoreId)),
                nameof(SelectListItem.Value),
                nameof(SelectListItem.Text), selectedStoreId);
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

        private LightningSupportedPaymentMethod GetExistingLightningSupportedPaymentMethod(string cryptoCode,
            StoreData store)
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
    
    public class RequestLightningChannelViewModel
    {
            
        [Display(Name = "Lightning node to open channel to")]
        [Required] public string TargetNodeInfo { get; set; }
            
        [Display(Name = "Outbound sats for channel")]
        [Required] public long Satoshis { get; set; }
        public string Description { get; set; }
        public string ServiceName { get; set; }
        public string Callback { get; set; }
            
        [Display(Name = "Store")]
        [Required] public string StoreId { get; set; }
        public SelectList Stores { get; set; }
        [Display(Name = "Sats per byte fee for channel opening transaction")]
        public decimal FeeRate { get; set; }

        public string StatusMessage { get; set; }
    }
}
