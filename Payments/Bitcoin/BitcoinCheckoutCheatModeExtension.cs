using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using NBitcoin;
using ExchangeSharp.BinanceGroup;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinCheckoutCheatModeExtension : ICheckoutCheatModeExtension
    {
        private readonly Cheater _cheater;

        public BitcoinCheckoutCheatModeExtension(Cheater cheater, BTCPayNetwork network)
        {
            _cheater = cheater;
            Network = network;
            pmi = PaymentTypes.CHAIN.GetPaymentMethodId(Network.CryptoCode);
        }
        public BTCPayNetwork Network { get; }

        private PaymentMethodId pmi;

        public bool Handle(PaymentMethodId paymentMethodId)
        => paymentMethodId == pmi;

        public async Task<ICheckoutCheatModeExtension.MineBlockResult> MineBlock(ICheckoutCheatModeExtension.MineBlockContext mineBlockContext)
        {
            var cow = _cheater.GetCashCow(Network.CryptoCode);
            var blockRewardBitcoinAddress = await cow.GetNewAddressAsync();
            await cow.GenerateToAddressAsync(mineBlockContext.BlockCount, blockRewardBitcoinAddress);
            return new ICheckoutCheatModeExtension.MineBlockResult();
        }

        public async Task<ICheckoutCheatModeExtension.PayInvoiceResult> PayInvoice(ICheckoutCheatModeExtension.PayInvoiceContext payInvoiceContext)
        {
            var address = BitcoinAddress.Create(payInvoiceContext.PaymentPrompt.Destination, Network.NBitcoinNetwork);
            var txid = (await _cheater.GetCashCow(Network.CryptoCode).SendToAddressAsync(address, new Money(payInvoiceContext.Amount, MoneyUnit.BTC))).ToString();
            return new ICheckoutCheatModeExtension.PayInvoiceResult(txid);
        }
    }
}
