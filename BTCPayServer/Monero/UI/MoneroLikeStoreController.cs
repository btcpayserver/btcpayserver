using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Monero;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("stores/{storeId}/monerolike")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [Authorize(Policy = Policies.CanModifyStoreSettings.Key, AuthenticationSchemes = Policies.CookieAuthentication)]
    [Authorize(Policy = Policies.CanModifyServerSettings.Key, AuthenticationSchemes = Policies.CookieAuthentication)]
    public class MoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public MoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRPCProvider moneroRpcProvider,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = moneroRpcProvider;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreMoneroLikeConfiguration(string storeId)
        {
            var monero = StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<MoneroSupportedPaymentMethod>();
            
            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();


            return View(new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                {
                    var settings = monero.SingleOrDefault(method => method.CryptoCode == pair.Key);
                    _MoneroRpcProvider.Summaries.TryGetValue(pair.Key, out var summary);
                    return new MoneroLikePaymentMethodViewModel()
                    {
                        Enabled =
                            settings != null &&
                            !excludeFilters.Match(new PaymentMethodId(pair.Key, MoneroPaymentType.Instance)),
                        Summary = summary,
                        CryptoCode = pair.Key,
                        AccountIndex = settings?.AccountIndex?? 0
                    };
                })
            });

        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }
        
        public class MoneroLikePaymentMethodViewModel: MoneroSupportedPaymentMethod
        {
            public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public bool Enabled { get; set; } 
        }
        
    }
}
