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

namespace BTCPayServer.Components.LSPS1
{
    public class LSPS1ViewComponent : ViewComponent
    {
        private readonly ILogger<LSPS1ViewComponent> _logger;
        private readonly StoreRepository _storeRepository;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;

        public LSPS1ViewComponent(
            ILogger<LSPS1ViewComponent> logger,
            StoreRepository storeRepository,
            LightningClientFactoryService lightningClientFactory,
            BTCPayNetworkProvider networkProvider,
            PaymentMethodHandlerDictionary handlers,
            IOptions<LightningNetworkOptions> lightningNetworkOptions)
        {
            _logger = logger;
            _storeRepository = storeRepository;
            _lightningClientFactory = lightningClientFactory;
            _networkProvider = networkProvider;
            _handlers = handlers;
            _lightningNetworkOptions = lightningNetworkOptions;
        }

        public async Task<IViewComponentResult> InvokeAsync(string storeId)
        {
            _logger.LogInformation("[LSPS1View] Component invoked for storeId: {StoreId}", storeId);

            var viewModel = new LSPS1ViewModel
            {
                HasLiquidityReport = false
            };

            if (string.IsNullOrEmpty(storeId))
            {
                _logger.LogWarning("[LSPS1View] StoreId is null or empty.");
                return View(viewModel);
            }

            try
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store == null)
                {
                    _logger.LogWarning("[LSPS1View] Store with Id {StoreId} not found.", storeId);
                    return View(viewModel);
                }
                _logger.LogInformation("[LSPS1View] Found store: {StoreName}", store.StoreName);

                _logger.LogInformation("[LSPS1View] Attempting to get Lightning client for the store.");
                var client = GetLightningClient(store);
                if (client == null)
                {
                    _logger.LogWarning("[LSPS1View] Could not get Lightning client.");
                    viewModel.Message = "Could not connect to Lightning node";
                    return View(viewModel);
                }
                _logger.LogInformation("[LSPS1View] Successfully retrieved Lightning client of type: {ClientType}", client.GetType().Name);

                _logger.LogInformation("[LSPS1View] Calling Liquidity.CheckAsync...");
                var liquidityReport = await Liquidity.CheckAsync(client, _logger);

                if (liquidityReport != null)
                {
                    _logger.LogInformation("[LSPS1View] Liquidity report received. Status: {Status}", liquidityReport.Liquidity_Status);
                    viewModel.HasLiquidityReport = true;
                    viewModel.LiquidityReport = liquidityReport;
                    viewModel.Message = "Liquidity information is available for your Core Lightning node";
                }
                else
                {
                    _logger.LogInformation("[LSPS1View] No liquidity report was generated. This may be because the node is not a CLightning node or an error occurred.");
                    viewModel.Message = "Liquidity information is only available for Core Lightning nodes";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LSPS1View] Error getting liquidity information for store {StoreId}", storeId);
                viewModel.Message = "Error retrieving liquidity information: " + ex.Message;
            }

            return View(viewModel);
        }

        private ILightningClient GetLightningClient(StoreData store)
        {
            _logger.LogInformation("[LSPS1View.GetClient] Starting to get lightning client...");
            try
            {
                var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
                if (network == null)
                {
                    _logger.LogError("[LSPS1View.GetClient] BTC network not found.");
                    return null;
                }

                var paymentMethod = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
                _logger.LogInformation("[LSPS1View.GetClient] Looking for handler for payment method: {PaymentMethod}", paymentMethod);
                if (_handlers.TryGet(paymentMethod) is not LightningLikePaymentHandler handler)
                {
                    _logger.LogError("[LSPS1View.GetClient] LightningLikePaymentHandler not available.");
                    return null;
                }
                _logger.LogInformation("[LSPS1View.GetClient] Found handler of type: {HandlerType}", handler.GetType().Name);

                var paymentMethodDetails = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(paymentMethod, _handlers);
                if (paymentMethodDetails == null)
                {
                    _logger.LogError("[LSPS1View.GetClient] No Lightning payment method configuration found for this store.");
                    return null;
                }
                _logger.LogInformation("[LSPS1View.GetClient] Found payment method details.");

                try
                {
                    var hasConnectionString = !string.IsNullOrEmpty(paymentMethodDetails.ConnectionString);
                    _logger.LogInformation("[LSPS1View.GetClient] Connection string is present: {HasConnectionString}", hasConnectionString);

                    if (!hasConnectionString)
                    {
                        _logger.LogInformation("[LSPS1View.GetClient] Connection string is empty, checking for internal node...");
                        if (_lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode, out var internalClient))
                        {
                            _logger.LogInformation("[LSPS1View.GetClient] Found and returning internal lightning node client.");
                            return internalClient;
                        }

                        _logger.LogError("[LSPS1View.GetClient] No internal lightning node found for {CryptoCode}", network.CryptoCode);
                        return null;
                    }

                    _logger.LogInformation("[LSPS1View.GetClient] Creating Lightning client with connection string via factory...");
                    var factoryClient = _lightningClientFactory.Create(paymentMethodDetails.ConnectionString, network);
                    _logger.LogInformation("[LSPS1View.GetClient] Successfully created client from factory.");
                    return factoryClient;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LSPS1View.GetClient] Error creating Lightning client from details.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LSPS1View.GetClient] General error getting Lightning client.");
                return null;
            }
        }
    }
}