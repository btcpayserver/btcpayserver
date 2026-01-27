#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Configuration;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Options;
using NBitcoin;


namespace BTCPayServer.Components.Liquidity
{
    /// <summary>
    /// ViewComponent that displays Lightning Network inbound liquidity information.
    /// Currently only supports Core Lightning (CLN) nodes.
    /// </summary>
    public class LiquidityViewComponent : ViewComponent
    {
        private readonly StoreRepository _storeRepository;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;

        public LiquidityViewComponent(
            StoreRepository storeRepository,
            LightningClientFactoryService lightningClientFactory,
            BTCPayNetworkProvider networkProvider,
            PaymentMethodHandlerDictionary handlers,
            IOptions<LightningNetworkOptions> lightningNetworkOptions)
        {
            _storeRepository = storeRepository;
            _lightningClientFactory = lightningClientFactory;
            _networkProvider = networkProvider;
            _handlers = handlers;
            _lightningNetworkOptions = lightningNetworkOptions;
        }

        public async Task<IViewComponentResult> InvokeAsync(string storeId)
        {
            var viewModel = new LiquidityViewModel();
            if (string.IsNullOrEmpty(storeId))
            {
                return View(viewModel);
            }
            try
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store == null)
                {
                    return View(viewModel);
                }

                var client = GetLightningClient(store);
                if (client == null)
                {
                    viewModel.Message = "Could not connect to Lightning node";
                    return View(viewModel);
                }

                var liquidityReport = await Liquidity.CheckAsync(client);
                if (liquidityReport != null)
                {
                    viewModel.HasLiquidityReport = true;
                    viewModel.LiquidityReport = liquidityReport;
                    viewModel.Message = "Liquidity information is available for your Core Lightning node";
                }
                else
                {
                    viewModel.Message = "Liquidity information is only available for Core Lightning nodes";
                }
            }
            catch (Exception)
            {
                viewModel.Message = "Error retrieving liquidity information";
            }

            return View(viewModel);
        }

        private ILightningClient? GetLightningClient(StoreData store)
        {
            try
            {
                var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
                if (network == null)
                {
                    return null;
                }

                var paymentMethodId = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
                if (_handlers.TryGet(paymentMethodId) is not LightningLikePaymentHandler)
                {
                    return null;
                }

                var paymentMethodDetails = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(paymentMethodId, _handlers);
                if (paymentMethodDetails == null)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(paymentMethodDetails.ConnectionString))
                {
                    return _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode, out var internalClient)
                        ? internalClient
                        : null;
                }

                return _lightningClientFactory.Create(paymentMethodDetails.ConnectionString, network);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}