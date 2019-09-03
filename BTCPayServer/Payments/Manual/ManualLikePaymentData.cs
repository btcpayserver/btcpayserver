using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{
    public class ManualPaymentData : CryptoPaymentData
    {
        public string GetPaymentId()
        {
            return Id;
        }

        public string[] GetSearchTerms()
        {
            return new[] {GetPaymentId()};
        }

        public decimal GetValue()
        {
            return Amount;
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return Confirmed;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            return Confirmed;
        }

        public PaymentType GetPaymentType()
        {
            return PaymentTypes.Manual;
        }

        public string GetDestination()
        {
            return string.Empty;
        }

        public ManualPaymentData()
        {
        }

        public string Id { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
        public string Notes { get; set; }
        public bool Confirmed { get; set; }
    }
}
