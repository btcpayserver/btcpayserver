using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Payments.Changelly
{
    public static class ChangellyPaymentModelExtensions
    {
        public static void PreparePaymentModelWithChangelly(this PaymentModel model, StoreBlob storeBlob,
            PaymentMethodAccounting accounting)
        {
            var changelly = (storeBlob.ChangellySettings != null && storeBlob.ChangellySettings.Enabled &&
                             storeBlob.ChangellySettings.IsConfigured())
                ? storeBlob.ChangellySettings
                : null;

            var coinswitch = (storeBlob.CoinSwitchSettings != null &&
                              storeBlob.CoinSwitchSettings.Enabled &&
                              storeBlob.CoinSwitchSettings.IsConfigured())
                ? storeBlob.CoinSwitchSettings
                : null;

            var changellyAmountDue = changelly != null
                ? (accounting.Due.ToDecimal(MoneyUnit.BTC) *
                   (1m + (changelly.AmountMarkupPercentage / 100m)))
                : (decimal?) null;

            model.ChangellyEnabled = changelly != null;
            model.ChangellyMerchantId = changelly?.ChangellyMerchantId;
            model.ChangellyAmountDue = changellyAmountDue;
            model.CoinSwitchEnabled = coinswitch != null;
            model.CoinSwitchAmountMarkupPercentage = coinswitch?.AmountMarkupPercentage ?? 0;
            model.CoinSwitchMerchantId = coinswitch?.MerchantId;
            model.CoinSwitchMode = coinswitch?.Mode;
        }
    }
}
