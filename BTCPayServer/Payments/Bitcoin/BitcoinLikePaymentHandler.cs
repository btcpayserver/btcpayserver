using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Org.BouncyCastle.Math.EC.ECCurve;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Payments.Bitcoin
{
    public interface IHasNetwork
    {
        BTCPayNetwork Network { get; }
    }
    public class BitcoinLikePaymentHandler : IPaymentMethodHandler, IHasNetwork
    {
        readonly ExplorerClientProvider _ExplorerProvider;
        private readonly BTCPayNetwork _Network;
        private readonly IFeeProviderFactory _FeeRateProviderFactory;
        private readonly NBXplorerDashboard _dashboard;
        private readonly WalletRepository _walletRepository;
        private readonly Services.Wallets.BTCPayWalletProvider _WalletProvider;
        
        public JsonSerializer Serializer { get; }
        public PaymentMethodId PaymentMethodId { get; private set; }
        public BTCPayNetwork Network => _Network;

        public BitcoinLikePaymentHandler(
            PaymentMethodId paymentMethodId,
            ExplorerClientProvider provider,
            BTCPayNetwork network,
            IFeeProviderFactory feeRateProviderFactory,
            DisplayFormatter displayFormatter,
            NBXplorerDashboard dashboard,
            WalletRepository walletRepository,
            Services.Wallets.BTCPayWalletProvider walletProvider)
        {
            Serializer = BlobSerializer.CreateSerializer(network.NBXplorerNetwork).Serializer;
            _ExplorerProvider = provider;
            _Network = network;
            PaymentMethodId = paymentMethodId;
            _FeeRateProviderFactory = feeRateProviderFactory;
            _dashboard = dashboard;
            _walletRepository = walletRepository;
            _WalletProvider = walletProvider;
        }

        class Prepare
        {
            public Task<FeeRate> GetRecommendedFeeRate;
            public Task<FeeRate> GetNetworkFeeRate;
            public Task<KeyPathInformation> ReserveAddress;
            public DerivationSchemeSettings DerivationSchemeSettings;
        }

        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }

        public BitcoinPaymentPromptDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<BitcoinPaymentPromptDetails>(Serializer);
        }
        public DerivationSchemeSettings ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<DerivationSchemeSettings>(Serializer) ?? throw new FormatException($"Invalid {nameof(DerivationSchemeSettings)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }
        public void StripDetailsForNonOwner(object details)
        {
            ((BitcoinPaymentPromptDetails)details).AccountDerivation = null;
        }
        public async Task AfterSavingInvoice(PaymentMethodContext paymentMethodContext)
        {
            var paymentPrompt = paymentMethodContext.Prompt;
            var store = paymentMethodContext.Store;
            var entity = paymentMethodContext.InvoiceEntity;
            var links = new List<WalletObjectLinkData>();
            var walletId = new WalletId(store.Id, _Network.CryptoCode);
            await _walletRepository.EnsureWalletObject(new WalletObjectId(
                walletId,
                WalletObjectData.Types.Invoice,
                entity.Id
                ));
            if (paymentPrompt.Destination is string)
            {
                links.Add(WalletRepository.NewWalletObjectLinkData(new WalletObjectId(
                        walletId,
                        WalletObjectData.Types.Address,
                        paymentPrompt.Destination),
                    new WalletObjectId(
                        walletId,
                        WalletObjectData.Types.Invoice,
                        entity.Id)));
            }
            await _walletRepository.EnsureCreated(null, links);
        }

        public Task BeforeFetchingRates(PaymentMethodContext paymentMethodContext)
        {
            paymentMethodContext.Prompt.Currency = _Network.CryptoCode;
            paymentMethodContext.Prompt.Divisibility = _Network.Divisibility;
            if (paymentMethodContext.Prompt.Activated)
            {
                var settings = ParsePaymentMethodConfig(paymentMethodContext.PaymentMethodConfig);
                var storeBlob = paymentMethodContext.StoreBlob;
                var store = paymentMethodContext.Store;
                paymentMethodContext.State = new Prepare()
                {
                    GetRecommendedFeeRate =
                        _FeeRateProviderFactory.CreateFeeProvider(_Network)
                            .GetFeeRateAsync(storeBlob.RecommendedFeeBlockTarget),
                    GetNetworkFeeRate = storeBlob.NetworkFeeMode == NetworkFeeMode.Never
                        ? null
                        : _FeeRateProviderFactory.CreateFeeProvider(_Network).GetFeeRateAsync(),
                    ReserveAddress = _WalletProvider.GetWallet(_Network)
                        .ReserveAddressAsync(store.Id, settings.AccountDerivation, "invoice"),
                    DerivationSchemeSettings = settings
                };
            }
            return Task.CompletedTask;
        }
        public async Task ConfigurePrompt(PaymentMethodContext paymentContext)
        {
            var prepare = (Prepare)paymentContext.State;
            var accountDerivation = prepare.DerivationSchemeSettings.AccountDerivation;
            if (!_ExplorerProvider.IsAvailable(_Network))
                throw new PaymentMethodUnavailableException($"Full node not available");
            var paymentMethod = paymentContext.Prompt;
            var onchainMethod = new BitcoinPaymentPromptDetails();
            var blob = paymentContext.StoreBlob;

            onchainMethod.FeeMode = blob.NetworkFeeMode;
            onchainMethod.RecommendedFeeRate = await prepare.GetRecommendedFeeRate;
            switch (onchainMethod.FeeMode)
            {
                case NetworkFeeMode.Always:
                case NetworkFeeMode.MultiplePaymentsOnly:
                    onchainMethod.PaymentMethodFeeRate = (await prepare.GetNetworkFeeRate);
                    if (onchainMethod.FeeMode == NetworkFeeMode.Always || paymentMethod.Calculate().TxCount > 0)
                    {
                        paymentMethod.PaymentMethodFee =
                            onchainMethod.PaymentMethodFeeRate.GetFee(100).GetValue(_Network); // assume price for 100 bytes
                    }
                    break;
                case NetworkFeeMode.Never:
                    onchainMethod.PaymentMethodFeeRate = FeeRate.Zero;
                    break;
            }
            if (paymentContext.InvoiceEntity.Type != InvoiceType.TopUp)
            {
                var txOut = _Network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTxOut();
                txOut.ScriptPubKey =
                    new Key().GetScriptPubKey(accountDerivation.ScriptPubKeyType());
                var dust = txOut.GetDustThreshold();
                var amount = paymentMethod.Calculate().Due;
                if (amount < dust.ToDecimal(MoneyUnit.BTC))
                    throw new PaymentMethodUnavailableException("Amount below the dust threshold. For amounts of this size, it is recommended to enable an off-chain (Lightning) payment method");
            }

            var reserved = await prepare.ReserveAddress;

            paymentMethod.Destination = reserved.Address.ToString();
            paymentContext.TrackedDestinations.Add(Network.GetTrackedDestination(reserved.Address.ScriptPubKey));
            onchainMethod.KeyPath = reserved.KeyPath;
            onchainMethod.AccountDerivation = accountDerivation;
            onchainMethod.PayjoinEnabled = blob.PayJoinEnabled &&
                                           accountDerivation.ScriptPubKeyType() != ScriptPubKeyType.Legacy &&
                                           _Network.SupportPayJoin;
            var logs = paymentContext.Logs;
            if (onchainMethod.PayjoinEnabled)
            {
                var isHotwallet = prepare.DerivationSchemeSettings.IsHotWallet;
                var nodeSupport = _dashboard?.Get(_Network.CryptoCode)?.Status?.BitcoinStatus?.Capabilities
                    ?.CanSupportTransactionCheck is true;
                onchainMethod.PayjoinEnabled &= isHotwallet && nodeSupport;
                if (!isHotwallet)
                    logs.Write("Payjoin should have been enabled, but your store is not a hotwallet", InvoiceEventData.EventSeverity.Warning);
                if (!nodeSupport)
                    logs.Write("Payjoin should have been enabled, but your version of NBXplorer or full node does not support it.", InvoiceEventData.EventSeverity.Warning);
                if (onchainMethod.PayjoinEnabled)
                    logs.Write("Payjoin is enabled for this invoice.", InvoiceEventData.EventSeverity.Info);
            }

            paymentMethod.Details = JObject.FromObject(onchainMethod, Serializer);
        }

        public static DerivationStrategyBase GetAccountDerivation(JToken activationData, BTCPayNetwork network)
        {
            if (activationData is JValue { Type: JTokenType.String, Value: string v })
            {
                var parser = network.GetDerivationSchemeParser();
                return parser.Parse(v);
            }
            throw new FormatException($"{network.CryptoCode}: Invalid activation data, impossible to parse the derivation scheme");
        }
        public static DerivationStrategyBase GetAccountDerivation(IDictionary<PaymentMethodId, JToken> activationDataByPmi, BTCPayNetwork network)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            activationDataByPmi.TryGetValue(pmi, out var value);
            if (value is null)
                return null;
            return GetAccountDerivation(value, network);
        }
        public Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
        {
            var parser = Network.GetDerivationSchemeParser();
            DerivationSchemeSettings settings = new DerivationSchemeSettings();
            if (parser.TryParseXpub(validationContext.Config.ToString(), ref settings))
            {
                validationContext.Config = JToken.FromObject(settings, Serializer);
                return Task.CompletedTask;
            }
            var res = validationContext.Config.ToObject<DerivationSchemeSettings>(Serializer);
            if (res is null)
            {
                validationContext.ModelState.AddModelError(nameof(validationContext.Config), "Invalid derivation scheme settings");
                return Task.CompletedTask;
            }
            if (res.AccountDerivation is null)
            {
                validationContext.ModelState.AddModelError(nameof(res.AccountDerivation), "Invalid account derivation");
            }
            return Task.CompletedTask;
        }

        public BitcoinLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<BitcoinLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(BitcoinLikePaymentData)}");
        }

        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
