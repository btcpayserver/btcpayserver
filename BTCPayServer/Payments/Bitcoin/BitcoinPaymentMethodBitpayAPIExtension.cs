using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Models;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinPaymentMethodBitpayAPIExtension : IPaymentMethodBitpayAPIExtension
    {
        public BitcoinPaymentMethodBitpayAPIExtension(
      PaymentMethodId paymentMethodId,
      IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
      PaymentMethodHandlerDictionary handlers)
        {
            PaymentMethodId = paymentMethodId;
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
            handler = (BitcoinLikePaymentHandler)handlers[paymentMethodId];
        }
        public PaymentMethodId PaymentMethodId { get; }

        private IPaymentLinkExtension paymentLinkExtension;
        private BitcoinLikePaymentHandler handler;

        public void PopulateCryptoInfo(Services.Invoices.InvoiceCryptoInfo cryptoInfo, InvoiceResponse dto, PaymentPrompt prompt, IUrlHelper urlHelper)
        {
            var accounting = prompt.Calculate();
            cryptoInfo.PaymentUrls = new Services.Invoices.InvoiceCryptoInfo.InvoicePaymentUrls()
            {
                BIP21 = paymentLinkExtension.GetPaymentLink(prompt, urlHelper),
            };
            var minerInfo = new MinerFeeInfo();
            minerInfo.TotalFee = accounting.ToSmallestUnit(accounting.PaymentMethodFee);

            minerInfo.SatoshiPerBytes = handler.ParsePaymentPromptDetails(prompt.Details).RecommendedFeeRate.SatoshiPerByte;
            dto.MinerFees.TryAdd(cryptoInfo.CryptoCode, minerInfo);

#pragma warning disable 618
            if (prompt.Currency == "BTC")
            {
                dto.BTCPrice = cryptoInfo.Price;
                dto.Rate = cryptoInfo.Rate;
                dto.ExRates = cryptoInfo.ExRates;
                dto.BitcoinAddress = cryptoInfo.Address;
                dto.BTCPaid = cryptoInfo.Paid;
                dto.BTCDue = cryptoInfo.Due;
                dto.PaymentUrls = cryptoInfo.PaymentUrls;
            }
#pragma warning restore 618
        }
    }
}
