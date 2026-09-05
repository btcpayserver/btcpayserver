using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.External;

public class ExternalPaymentMethodHandler : IPaymentMethodHandler
{
    private readonly CurrencyNameTable _currencyNameTable;

    public ExternalPaymentMethodHandler(PaymentMethodId paymentMethodId, CurrencyNameTable currencyNameTable)
    {
        PaymentMethodId = paymentMethodId;
        _currencyNameTable = currencyNameTable;
        Serializer = BlobSerializer.CreateSerializer((NBitcoin.Network?)null).Serializer;
    }

    public PaymentMethodId PaymentMethodId { get; }
    public Newtonsoft.Json.JsonSerializer Serializer { get; }

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Inactive = false;
        context.Prompt.Currency = context.InvoiceEntity.Currency;
        var divisibility = _currencyNameTable.GetNumberFormatInfoOrDefault(context.InvoiceEntity.Currency).CurrencyDecimalDigits;
        context.Prompt.Divisibility = divisibility;
        context.Prompt.RateDivisibility = divisibility;
        context.Prompt.PaymentMethodFee = 0m;
        return Task.CompletedTask;
    }

    public Task ConfigurePrompt(PaymentMethodContext context)
    {
        context.Prompt.Details = JObject.FromObject(new ExternalPaymentPromptDetails(), Serializer);
        return Task.CompletedTask;
    }

    public ExternalPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<ExternalPaymentMethodConfig>(Serializer) ?? new ExternalPaymentMethodConfig();
    }
    object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config) => ParsePaymentMethodConfig(config);

    public ExternalPaymentPromptDetails ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<ExternalPaymentPromptDetails>(Serializer) ?? new ExternalPaymentPromptDetails();
    }
    object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details) => ParsePaymentPromptDetails(details);

    public ExternalPaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<ExternalPaymentData>(Serializer)
               ?? throw new System.FormatException($"Invalid {nameof(ExternalPaymentData)}");
    }
    object IPaymentMethodHandler.ParsePaymentDetails(JToken details) => ParsePaymentDetails(details);
}
