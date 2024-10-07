using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Common;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Rating;
using BTCPayServer.Services.Altcoins.Zcash.RPC.Models;
using BTCPayServer.Services.Altcoins.Zcash.Services;
using BTCPayServer.Services.Altcoins.Zcash.Utils;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly ZcashLikeSpecificBtcPayNetwork _network;
        public ZcashLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly ZcashRPCProvider _ZcashRpcProvider;
        public PaymentMethodId PaymentMethodId { get; }
        public ZcashLikePaymentMethodHandler(BTCPayNetworkBase network, ZcashRPCProvider ZcashRpcProvider)
        {
            _network = (ZcashLikeSpecificBtcPayNetwork)network;
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(_network.CryptoCode);
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _ZcashRpcProvider = ZcashRpcProvider;
        }
        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated)
            {
                var walletClient = _ZcashRpcProvider.WalletRpcClients[_network.CryptoCode];
                var daemonClient = _ZcashRpcProvider.DaemonRpcClients[_network.CryptoCode];
                var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                context.State = new Prepare()
                {
                    GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                    ReserveAddress = s => walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() { Label = $"btcpay invoice #{s}", AccountIndex = config.AccountIndex }),
                    AccountIndex = config.AccountIndex
                };
            }
            return Task.CompletedTask;
        }
        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_ZcashRpcProvider.IsAvailable(_network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = context.InvoiceEntity;
            var ZcashPrepare = (Prepare)context.State;
            var feeRatePerKb = await ZcashPrepare.GetFeeRate;
            var address = await ZcashPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRatePerKb.Fee / 1024;

            context.Prompt.PaymentMethodFee = ZcashMoney.Convert(feeRatePerByte * 100);
            context.Prompt.Details = JObject.FromObject(new ZcashPaymentPromptDetails()
            {
                AccountIndex = ZcashPrepare.AccountIndex,
                AddressIndex = address.AddressIndex,
                DepositAddress = address.Address
            }, Serializer);
            context.TrackedDestinations.Add(address.Address);
        }

        object IPaymentMethodHandler.ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }
        public ZcashPaymentPromptDetails ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return details.ToObject<ZcashPaymentPromptDetails>(Serializer);
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }
        public ZcashPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<ZcashPaymentMethodConfig>(Serializer) ?? throw new FormatException($"Invalid {nameof(ZcashPaymentMethodConfig)}");
        }



        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;
            public long AccountIndex { get; internal set; }
        }

        public ZcashLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<ZcashLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(ZcashLikePaymentData)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
