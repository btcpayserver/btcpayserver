#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinCheckoutModelExtension : ICheckoutModelExtension
    {
        public const string CheckoutBodyComponentName = "BitcoinCheckoutBody";
        private readonly BTCPayNetwork _Network;
        private readonly DisplayFormatter _displayFormatter;
        private readonly IPaymentLinkExtension paymentLinkExtension;
        private readonly IPaymentLinkExtension? lnPaymentLinkExtension;
        private readonly IPaymentLinkExtension? lnurlPaymentLinkExtension;
        private readonly string? _bech32Prefix;
        private readonly PaymentMethodId lnPmi;
        private readonly PaymentMethodId lnurlPmi;

        public BitcoinCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            BTCPayNetwork network,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            DisplayFormatter displayFormatter)
        {
            PaymentMethodId = paymentMethodId;
            _Network = network;
            _displayFormatter = displayFormatter;
            lnPmi = PaymentTypes.LN.GetPaymentMethodId(_Network.CryptoCode);
            lnurlPmi = PaymentTypes.LNURL.GetPaymentMethodId(_Network.CryptoCode);
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
            lnPaymentLinkExtension = paymentLinkExtensions.SingleOrDefault(p => p.PaymentMethodId == lnPmi);
            lnurlPaymentLinkExtension = paymentLinkExtensions.SingleOrDefault(p => p.PaymentMethodId == lnurlPmi);
            _bech32Prefix = network.NBitcoinNetwork.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, false) is { } enc ? Encoders.ASCII.EncodeData(enc.HumanReadablePart) : null;
        }
        public string Image => _Network.CryptoImagePath;
        public string Badge => "";
        public PaymentMethodId PaymentMethodId { get; }
        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            var handler = context.Handler;
            var prompt = context.Prompt;
            if (handler.ParsePaymentPromptDetails(prompt.Details) is not BitcoinPaymentPromptDetails details)
                return;
            context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;
            context.Model.ShowRecommendedFee = context.StoreBlob.ShowRecommendedFee;
            context.Model.FeeRate = details.RecommendedFeeRate.SatoshiPerByte;


            var bip21Case = _Network.SupportLightning && context.StoreBlob.OnChainWithLnInvoiceFallback;
            var amountInSats = bip21Case && context.StoreBlob.LightningAmountInSatoshi && context.Model.PaymentMethodCurrency == "BTC";
            string? lightningFallback = null;
            if (bip21Case)
            {
                var lnPrompt = context.InvoiceEntity.GetPaymentPrompt(lnPmi);
                if (lnPrompt is { Destination: not null })
                {
                    var lnUrl = lnPaymentLinkExtension?.GetPaymentLink(lnPrompt, context.UrlHelper);
                    if (lnUrl is not null)
                        lightningFallback = lnUrl;
                }
                else
                {
                    var lnurlPrompt = context.InvoiceEntity.GetPaymentPrompt(lnurlPmi);
                    var lnUrl = lnurlPrompt is null
                        ? null
                        : lnurlPaymentLinkExtension?.GetPaymentLink(lnurlPrompt, context.UrlHelper);
                    if (lnUrl is not null)
                        lightningFallback = lnUrl;

                }
                if (!string.IsNullOrEmpty(lightningFallback))
                {
                    lightningFallback = lightningFallback
                        .Replace("lightning:", "lightning=", StringComparison.OrdinalIgnoreCase);
                }
            }


            if (handler is BitcoinLikePaymentHandler bitcoinHandler)
            {
                var paymentData = context.InvoiceEntity.GetAllBitcoinPaymentData(bitcoinHandler, true)?.MinBy(o => o.ConfirmationCount);
                if (paymentData is not null)
                {
                    context.Model.RequiredConfirmations = NBXplorerListener.ConfirmationRequired(context.InvoiceEntity, paymentData);
                    context.Model.ReceivedConfirmations = paymentData.ConfirmationCount;
                }
            }

            // We're leading the way in Bitcoin community with adding UPPERCASE Bech32 addresses in QR Code
            //
            // Correct casing: Addresses in payment URI need to be …
            // - lowercase in link version
            // - uppercase in QR version
            //
            // The keys (e.g. "bitcoin:" or "lightning=" should be lowercase!

            // cryptoInfo.PaymentUrls?.BIP21: bitcoin:bcrt1qxp2qa5?amount=0.00044007
            var bip21 = paymentLinkExtension.GetPaymentLink(prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrl = context.Model.InvoiceBitcoinUrlQR = bip21 ?? "";
            // model.InvoiceBitcoinUrl: bitcoin:bcrt1qxp2qa5?amount=0.00044007
            // model.InvoiceBitcoinUrlQR: bitcoin:bcrt1qxp2qa5?amount=0.00044007

            if (!string.IsNullOrEmpty(lightningFallback))
            {
                var delimiterUrl = context.Model.InvoiceBitcoinUrl.Contains("?") ? "&" : "?";
                context.Model.InvoiceBitcoinUrl += $"{delimiterUrl}{lightningFallback}";
                // model.InvoiceBitcoinUrl: bitcoin:bcrt1qxp2qa5dhn7?amount=0.00044007&lightning=lnbcrt440070n1...

                var delimiterUrlQR = context.Model.InvoiceBitcoinUrlQR.Contains("?") ? "&" : "?";
                context.Model.InvoiceBitcoinUrlQR += $"{delimiterUrlQR}{lightningFallback.ToUpperInvariant().Replace("LIGHTNING=", "lightning=", StringComparison.OrdinalIgnoreCase)}";
                // model.InvoiceBitcoinUrlQR: bitcoin:bcrt1qxp2qa5dhn7?amount=0.00044007&lightning=LNBCRT4400...
            }

            if (_Network.CryptoCode.Equals("BTC", StringComparison.InvariantCultureIgnoreCase) && _bech32Prefix is not null && context.Model.Address.StartsWith(_bech32Prefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrlQR.Replace(
                    $"{_Network.NBitcoinNetwork.UriScheme}:{context.Model.Address}", $"{_Network.NBitcoinNetwork.UriScheme}:{context.Model.Address.ToUpperInvariant()}",
                    StringComparison.OrdinalIgnoreCase);
                // model.InvoiceBitcoinUrlQR: bitcoin:BCRT1QXP2QA5DHN...?amount=0.00044007&lightning=LNBCRT4400...
            }
           

            if (amountInSats)
            {
                PreparePaymentModelForAmountInSats(context.Model, context.Prompt.Rate, _displayFormatter);
            }
        }

        public static void PreparePaymentModelForAmountInSats(CheckoutModel model, decimal rate, DisplayFormatter displayFormatter)
        {
            var satoshiCulture = new CultureInfo(CultureInfo.InvariantCulture.Name)
            {
                NumberFormat = { NumberGroupSeparator = " " }
            };
            model.PaymentMethodCurrency = "sats";
            model.Due = Money.Parse(model.Due).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.Paid = Money.Parse(model.Paid).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.OrderAmount = Money.Parse(model.OrderAmount).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.NetworkFee = new Money(model.NetworkFee, MoneyUnit.BTC).ToUnit(MoneyUnit.Satoshi);
            model.Rate = model.InvoiceCurrency is "BTC" or "SATS"
                ? null
                : displayFormatter.Currency(rate / 100_000_000, model.InvoiceCurrency, DisplayFormatter.CurrencyFormat.Symbol);
        }

    }
}
