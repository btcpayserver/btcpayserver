using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using static BTCPayServer.Payments.ICheckoutCheatModeExtension;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningCheckoutCheatModeExtension : ICheckoutCheatModeExtension
    {
        private readonly Cheater _cheater;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly PaymentMethodId[] pmis;

        public LightningCheckoutCheatModeExtension(Cheater cheater, BTCPayNetwork network, LightningClientFactoryService lightningClientFactoryService)
        {
            _cheater = cheater;
            Network = network;
            _lightningClientFactoryService = lightningClientFactoryService;
            pmis = [PaymentTypes.LNURL.GetPaymentMethodId(Network.CryptoCode), PaymentTypes.LN.GetPaymentMethodId(Network.CryptoCode)];
        }
        public BTCPayNetwork Network { get; }

        public bool Handle(PaymentMethodId paymentMethodId)
        => pmis.Contains(paymentMethodId);

        public Task<ICheckoutCheatModeExtension.MineBlockResult> MineBlock(ICheckoutCheatModeExtension.MineBlockContext mineBlockContext)
        => new Bitcoin.BitcoinCheckoutCheatModeExtension(_cheater, Network).MineBlock(mineBlockContext);

        public async Task<ICheckoutCheatModeExtension.PayInvoiceResult> PayInvoice(ICheckoutCheatModeExtension.PayInvoiceContext payInvoiceContext)
        {
            // requires the channels to be set up using the BTCPayServer.Tests/docker-lightning-channel-setup.sh script
            var lnClient = _lightningClientFactoryService.Create(
            Environment.GetEnvironmentVariable("BTCPAY_BTCEXTERNALLNDREST"),
                Network);

            var destination = payInvoiceContext.PaymentPrompt.Destination;
            var lnAmount = new LightMoney(payInvoiceContext.Amount, LightMoneyUnit.BTC);
            var response = await lnClient.Pay(destination, new PayInvoiceParams { Amount = lnAmount });

            if (response.Result == PayResult.Ok)
            {
                var bolt11 = BOLT11PaymentRequest.Parse(destination, Network.NBitcoinNetwork);
                var paymentHash = bolt11.PaymentHash?.ToString();
                var paid = response.Details.TotalAmount.ToDecimal(LightMoneyUnit.BTC);
                return new PayInvoiceResult(paymentHash)
                {
                    SuccessMessage = $"Sent payment {paymentHash}"
                };
            };
            throw new Exception($"Error while paying through lightning: {(Status: response.Result, ErrorDetails: response.ErrorDetail)}");
        }
    }
}
