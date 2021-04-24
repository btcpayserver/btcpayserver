using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinLikePaymentHandler : PaymentMethodHandlerBase<DerivationSchemeSettings, BTCPayNetwork>
    {
        readonly ExplorerClientProvider _ExplorerProvider;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IFeeProviderFactory _FeeRateProviderFactory;
        private readonly NBXplorerDashboard _dashboard;
        private readonly Services.Wallets.BTCPayWalletProvider _WalletProvider;
        private readonly Dictionary<string, string> _bech32Prefix;

        public BitcoinLikePaymentHandler(ExplorerClientProvider provider,
            BTCPayNetworkProvider networkProvider,
            IFeeProviderFactory feeRateProviderFactory,
            NBXplorerDashboard dashboard,
            Services.Wallets.BTCPayWalletProvider walletProvider)
        {
            _ExplorerProvider = provider;
            _networkProvider = networkProvider;
            _FeeRateProviderFactory = feeRateProviderFactory;
            _dashboard = dashboard;
            _WalletProvider = walletProvider;

            _bech32Prefix = networkProvider.GetAll().OfType<BTCPayNetwork>()
                .Where(network => network.NBitcoinNetwork?.Consensus?.SupportSegwit is true).ToDictionary(network => network.CryptoCode,
                    network => Encoders.ASCII.EncodeData(
                        network.NBitcoinNetwork.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, false)
                            .HumanReadablePart));
            
        }

        class Prepare
        {
            public Task<FeeRate> GetFeeRate;
            public Task<FeeRate> GetNetworkFeeRate;
            public Task<KeyPathInformation> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);
            model.ShowRecommendedFee = storeBlob.ShowRecommendedFee;
            model.FeeRate = ((BitcoinLikeOnChainPaymentMethod) paymentMethod.GetPaymentMethodDetails()).GetFeeRate();
            model.PaymentMethodName = GetPaymentMethodName(network);

            var lightningFallback = "";
            if (model.Activated && network.SupportLightning && storeBlob.OnChainWithLnInvoiceFallback)
            {
                var lightningInfo = invoiceResponse.CryptoInfo.FirstOrDefault(a =>
                    a.GetpaymentMethodId() == new PaymentMethodId(model.CryptoCode, PaymentTypes.LightningLike));
                if (!string.IsNullOrEmpty(lightningInfo?.PaymentUrls?.BOLT11))
                    lightningFallback = "&" + lightningInfo.PaymentUrls.BOLT11.Replace("lightning:", "lightning=", StringComparison.OrdinalIgnoreCase);
            }

            if (model.Activated)
            {
                model.InvoiceBitcoinUrl = (cryptoInfo.PaymentUrls?.BIP21 ?? "") + lightningFallback;
                model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrl;
            }
            else
            {
                model.InvoiceBitcoinUrl = "";
                model.InvoiceBitcoinUrlQR = "";
            }

            // Most wallets still don't support BITCOIN: schema, so we're leaving this for better days
            // Ref: https://github.com/btcpayserver/btcpayserver/pull/2060#issuecomment-723828348
            //model.InvoiceBitcoinUrlQR = cryptoInfo.PaymentUrls.BIP21
            //    .Replace("bitcoin:", "BITCOIN:", StringComparison.OrdinalIgnoreCase)
            //    + lightningFallback.ToUpperInvariant().Replace("LIGHTNING=", "lightning=", StringComparison.OrdinalIgnoreCase);

            // We're leading the way in Bitcoin community with adding UPPERCASE Bech32 addresses in QR Code
            if (network.CryptoCode.Equals("BTC", StringComparison.InvariantCultureIgnoreCase) && _bech32Prefix.TryGetValue(model.CryptoCode, out var prefix) && model.BtcAddress.StartsWith(prefix,  StringComparison.OrdinalIgnoreCase))
            {
                model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrlQR.Replace(
                    $"{network.UriScheme}:{model.BtcAddress}", $"{network.UriScheme}:{model.BtcAddress.ToUpperInvariant()}",
                    StringComparison.OrdinalIgnoreCase
                );
            }
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        private string GetCryptoImage(BTCPayNetworkBase network)
        {
            return network.CryptoImagePath;
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider
                .GetAll()
                .OfType<BTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike));
        }

        private string GetPaymentMethodName(BTCPayNetworkBase network)
        {
            return network.DisplayName;
        }

        public override object PreparePayment(DerivationSchemeSettings supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            var storeBlob = store.GetStoreBlob();
            return new Prepare()
            {
                GetFeeRate =
                    _FeeRateProviderFactory.CreateFeeProvider(network)
                        .GetFeeRateAsync(storeBlob.RecommendedFeeBlockTarget),
                GetNetworkFeeRate = storeBlob.NetworkFeeMode == NetworkFeeMode.Never
                    ? null
                    : _FeeRateProviderFactory.CreateFeeProvider(network).GetFeeRateAsync(),
                ReserveAddress = _WalletProvider.GetWallet(network)
                    .ReserveAddressAsync(supportedPaymentMethod.AccountDerivation)
            };
        }

        public override PaymentType PaymentType => PaymentTypes.BTCLike;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            InvoiceLogs logs,
            DerivationSchemeSettings supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store,
            BTCPayNetwork network, object preparePaymentObject)
        {
            if (preparePaymentObject is null)
            {
                return new BitcoinLikeOnChainPaymentMethod()
                {
                    Activated = false
                };
            }
            if (!_ExplorerProvider.IsAvailable(network))
                throw new PaymentMethodUnavailableException($"Full node not available");
            var prepare = (Prepare)preparePaymentObject;
            var onchainMethod = new BitcoinLikeOnChainPaymentMethod();
            var blob = store.GetStoreBlob();
            onchainMethod.Activated = true;
            // TODO: this needs to be refactored to move this logic into BitcoinLikeOnChainPaymentMethod
            // This is likely a constructor code
            onchainMethod.NetworkFeeMode = blob.NetworkFeeMode;
            onchainMethod.FeeRate = await prepare.GetFeeRate;
            switch (onchainMethod.NetworkFeeMode)
            {
                case NetworkFeeMode.Always:
                    onchainMethod.NetworkFeeRate = (await prepare.GetNetworkFeeRate);
                    onchainMethod.NextNetworkFee =
                        onchainMethod.NetworkFeeRate.GetFee(100); // assume price for 100 bytes
                    break;
                case NetworkFeeMode.Never:
                    onchainMethod.NetworkFeeRate = FeeRate.Zero;
                    onchainMethod.NextNetworkFee = Money.Zero;
                    break;
                case NetworkFeeMode.MultiplePaymentsOnly:
                    onchainMethod.NetworkFeeRate = (await prepare.GetNetworkFeeRate);
                    onchainMethod.NextNetworkFee = Money.Zero;
                    break;
            }

            var reserved = await prepare.ReserveAddress;
            onchainMethod.DepositAddress = reserved.Address.ToString();
            onchainMethod.KeyPath = reserved.KeyPath;
            onchainMethod.PayjoinEnabled = blob.PayJoinEnabled &&
                                           supportedPaymentMethod
                                               .AccountDerivation.ScriptPubKeyType() != ScriptPubKeyType.Legacy &&
                                           network.SupportPayJoin;
            if (onchainMethod.PayjoinEnabled)
            {
                var prefix = $"{supportedPaymentMethod.PaymentId.ToPrettyString()}:";
                var nodeSupport = _dashboard?.Get(network.CryptoCode)?.Status?.BitcoinStatus?.Capabilities
                    ?.CanSupportTransactionCheck is true;
                onchainMethod.PayjoinEnabled &= supportedPaymentMethod.IsHotWallet && nodeSupport;
                if (!supportedPaymentMethod.IsHotWallet)
                    logs.Write($"{prefix} Payjoin should have been enabled, but your store is not a hotwallet", InvoiceEventData.EventSeverity.Warning);
                if (!nodeSupport)
                    logs.Write($"{prefix} Payjoin should have been enabled, but your version of NBXplorer or full node does not support it.", InvoiceEventData.EventSeverity.Warning);
                if (onchainMethod.PayjoinEnabled)
                    logs.Write($"{prefix} Payjoin is enabled for this invoice.", InvoiceEventData.EventSeverity.Info);
            }

            return onchainMethod;
        }
    }
}
