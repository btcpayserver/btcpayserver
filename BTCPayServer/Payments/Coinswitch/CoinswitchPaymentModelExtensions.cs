using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.CoinSwitch
{
    public static class CoinswitchPaymentModelExtensions
    {
        public static void PreparePaymentModelWithCoinswitch(this PaymentModel model, StoreBlob storeBlob,
            PaymentMethodAccounting accounting)
        {
            var coinswitch = (storeBlob.CoinSwitchSettings != null &&
                              storeBlob.CoinSwitchSettings.Enabled &&
                              storeBlob.CoinSwitchSettings.IsConfigured())
                ? storeBlob.CoinSwitchSettings
                : null;

            model.CoinSwitchEnabled = coinswitch != null;
            model.CoinSwitchAmountMarkupPercentage = coinswitch?.AmountMarkupPercentage ?? 0;
            model.CoinSwitchMerchantId = coinswitch?.MerchantId;
            model.CoinSwitchMode = coinswitch?.Mode;
        }
    }
}
