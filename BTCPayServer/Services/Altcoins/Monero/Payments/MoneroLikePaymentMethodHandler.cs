using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Rating;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Services.Altcoins.Monero.Services;
using BTCPayServer.Services.Altcoins.Monero.Utils;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly MoneroLikeSpecificBtcPayNetwork _network;
        public MoneroLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public PaymentMethodId PaymentMethodId { get; }

        public MoneroLikePaymentMethodHandler(MoneroLikeSpecificBtcPayNetwork network, MoneroRPCProvider moneroRpcProvider)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _moneroRpcProvider = moneroRpcProvider;
        }

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated)
            {
                var supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                var walletClient = _moneroRpcProvider.WalletRpcClients[_network.CryptoCode];
                var daemonClient = _moneroRpcProvider.DaemonRpcClients[_network.CryptoCode];
                context.State = new Prepare()
                {
                    GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                    ReserveAddress = s => walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() { Label = $"btcpay invoice #{s}", AccountIndex = supportedPaymentMethod.AccountIndex }),
                    AccountIndex = supportedPaymentMethod.AccountIndex
                };
            }
            return Task.CompletedTask;
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_moneroRpcProvider.IsAvailable(_network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = context.InvoiceEntity;
            Prepare moneroPrepare = (Prepare)context.State;
            var feeRatePerKb = await moneroPrepare.GetFeeRate;
            var address = await moneroPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRatePerKb.Fee / 1024;
            var details = new MoneroLikeOnChainPaymentMethodDetails()
            {
                AccountIndex = moneroPrepare.AccountIndex,
                AddressIndex = address.AddressIndex,
                InvoiceSettledConfirmationThreshold = ParsePaymentMethodConfig(context.PaymentMethodConfig).InvoiceSettledConfirmationThreshold
            };
            context.Prompt.Destination = address.Address;
            context.Prompt.PaymentMethodFee = MoneroMoney.Convert(feeRatePerByte * 100);
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(address.Address);
        }
        private MoneroPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<MoneroPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;

            public long AccountIndex { get; internal set; }
        }

        public MoneroLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return details.ToObject<MoneroLikeOnChainPaymentMethodDetails>(Serializer);
        }

        public MoneroLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<MoneroLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
