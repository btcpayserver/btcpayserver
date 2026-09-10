using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public class InvoiceCheckoutData
{
    public string Id { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public InvoiceType Type { get; set; }

    public string Currency { get; set; }

    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Amount { get; set; }

    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal PaidAmount { get; set; }

    public string CheckoutLink { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public InvoiceStatus Status { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public InvoiceExceptionStatus AdditionalStatus { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset MonitoringExpiration { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset ExpirationTime { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset CreatedTime { get; set; }

    public CheckoutOptions Checkout { get; set; } = new();
    public InvoiceDataBase.ReceiptOptions Receipt { get; set; } = new();
    public InvoicePaymentMethodDataModel[] PaymentMethods { get; set; } = Array.Empty<InvoicePaymentMethodDataModel>();

    public class CheckoutOptions
    {
        public string[] PaymentMethods { get; set; }
        public string DefaultPaymentMethod { get; set; }

        [JsonProperty("redirectURL")]
        public string RedirectURL { get; set; }

        public bool RedirectAutomatically { get; set; }
    }
}
