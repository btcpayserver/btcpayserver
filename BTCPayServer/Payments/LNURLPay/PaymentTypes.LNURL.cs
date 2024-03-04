using System;
using System.Collections.Generic;
using System.Globalization;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LNURLPayPaymentType : LightningPaymentType
    {
        public new static LNURLPayPaymentType Instance { get; } = new();
        public override string ToPrettyString() => "LNURL-Pay";
        public override string GetId() => "LNURL";
        public override string ToStringNormalized() => "LNURL";

        public override string InvoiceViewPaymentPartialName { get; } = "Lightning/ViewLightningLikePaymentData";

        public override bool IsPaymentType(string paymentType)
        {
            return IsPaymentTypeBase(paymentType);
        }
    }
}
