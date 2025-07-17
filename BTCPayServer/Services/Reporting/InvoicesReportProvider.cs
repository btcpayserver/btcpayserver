#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Reporting;

public class InvoicesReportProvider : ReportProvider
{
    public DisplayFormatter DisplayFormatter { get; }

    class MetadataFields
    {
        private readonly Dictionary<string, StoreReportResponse.Field> _dict = new();
        private readonly HashSet<string?> _baseFields = new();

        public MetadataFields(IList<StoreReportResponse.Field> viewDefinitionFields)
        {
            foreach (var field in viewDefinitionFields)
                _baseFields.Add(field.Name);
        }

        public List<StoreReportResponse.Field> Fields { get; } = new();
        public Dictionary<string, object?> Values { get; } = new();

        public void TryAdd(string fieldName, object? value, string? columnType = null)
        {
            var type = columnType ?? GetColumnType(value);
            if (type is null || _baseFields.Contains(fieldName))
                return;
            var field = new StoreReportResponse.Field(fieldName, type);
            if (_dict.TryAdd(fieldName, field))
            {
                Fields.Add(field);
            }
            else if (_dict[fieldName].Type != type)
                return;

            Values.TryAdd(fieldName, value);
        }

        private string? GetColumnType(object? value)
            => value switch
            {
                null => "text",
                bool _ => "boolean",
                string _ => "text",
                decimal or double or float or long or int _ => "number",
                DateTimeOffset _ => "datetime",
                // FormattedAmount
                JObject => "amount",
                _ => null
            };

        public void WriteValues(IList<object?> data)
        {
            foreach (var field in Fields)
            {
                data.Add(Values.TryGetValue(field.Name, out var value) ? value : null);
            }
        }

        public HashSet<string> CartItems = new();
        public void HasCartItem(string itemId) => CartItems.Add(itemId);
    }

    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;


    public override string Name { get; } = "Invoices";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery()
        {
            EndDate = queryContext.To,
            StartDate = queryContext.From,
            StoreId = new[] { queryContext.StoreId },
        }, cancellation);

        queryContext.ViewDefinition = new ViewDefinition()
        {
            Fields = new List<StoreReportResponse.Field>()
            {
                new("InvoiceCreatedDate", "datetime"),
                new("InvoiceId", "invoice_id"),
                new("InvoiceCurrency", "text"),
                new("InvoiceDue", "amount"),
                new("InvoicePrice", "amount"),
                new("InvoiceFullStatus", "text"),
                new("InvoiceStatus", "text"),
                new("InvoiceExceptionStatus", "text"),

                new("PaymentReceivedDate", "datetime"),
                new("PaymentId", "text"),
                new("PaymentRate", "amount"),
                new("PaymentAddress", "text"),
                new("PaymentMethodId", "text"),
                new("PaymentCurrency", "text"),
                new("PaymentAmount", "amount"),
                new("PaymentMethodFee", "amount"),
                new("PaymentInvoiceAmount", "amount"),
            }
        };

        var trackedCurrencies = (await _storeRepository.FindStore(queryContext.StoreId))?.GetStoreBlob().GetTrackedRates().ToHashSet() ?? new();

        var metadataFields = new MetadataFields(queryContext.ViewDefinition.Fields);
        foreach (var invoiceEntity in invoices)
        {
            var payments = invoiceEntity.GetPayments(true);
            metadataFields.Values.Clear();

            foreach (var currencyPair in
                     (from p in invoiceEntity
                                .GetPaymentPrompts()
                                .Select(c => c.Currency)
                         from c in trackedCurrencies.Concat([invoiceEntity.Currency])
                         where p != c
                         select new CurrencyPair(p, c)).Distinct())
            {
                if (!invoiceEntity.TryGetRate(currencyPair, out var rate))
                    metadataFields.TryAdd($"Rate ({currencyPair})", null, "number");
                else
                    metadataFields.TryAdd($"Rate ({currencyPair})", rate);
            }

            var firstPayment = payments.FirstOrDefault();
            if (firstPayment is not null)
            {
                FlattenFields(invoiceEntity.Metadata.ToJObject(), metadataFields, new(invoiceEntity, DisplayFormatter));
                Write(queryContext, invoiceEntity, firstPayment, true, metadataFields);
                foreach (var payment in payments.Skip(1))
                {
                    Write(queryContext, invoiceEntity, payment, false, metadataFields);
                }
            }
            else if (invoiceEntity is
                     not { Status: InvoiceStatus.Expired, ExceptionStatus: InvoiceExceptionStatus.None } and
                     not { Status: InvoiceStatus.New, ExceptionStatus: InvoiceExceptionStatus.None })
            {
                FlattenFields(invoiceEntity.Metadata.ToJObject(), metadataFields, new(invoiceEntity, DisplayFormatter));
                Write(queryContext, invoiceEntity, firstPayment, true, metadataFields);
            }
        }

        foreach (var mf in metadataFields.Fields)
        {
            queryContext.ViewDefinition.Fields.Add(mf);
        }
    }

    private void Write(
        QueryContext queryContext,
        InvoiceEntity parentEntity,
        PaymentEntity? payment, bool isFirst,
        MetadataFields metadataFields)
    {
        var data = queryContext.AddData();

        data.Add(parentEntity.InvoiceTime);
        data.Add(parentEntity.Id);
        var invoiceEntity = parentEntity;
        if (!isFirst)
            invoiceEntity = null;

        data.Add(invoiceEntity?.Currency);
        data.Add(invoiceEntity is null ? null : DisplayFormatter.ToFormattedAmount(invoiceEntity.NetDue, parentEntity.Currency));
        data.Add(invoiceEntity is null ? null : DisplayFormatter.ToFormattedAmount(invoiceEntity.Price, parentEntity.Currency));

        data.Add(invoiceEntity?.GetInvoiceState().ToString());
        data.Add(invoiceEntity?.Status.ToString());
        data.Add(invoiceEntity?.ExceptionStatus is null or InvoiceExceptionStatus.None ? "" : invoiceEntity.ExceptionStatus.ToString());


        data.Add(payment?.ReceivedTime);
        data.Add(payment?.Id);
        data.Add(payment?.Rate);
        data.Add(payment?.Destination);
        data.Add(payment?.PaymentMethodId.ToString());
        data.Add(payment?.Currency);
        data.Add(payment is null ? null : new FormattedAmount(payment.PaidAmount.Gross, payment.Divisibility).ToJObject());
        data.Add(payment is null ? null : new FormattedAmount(payment.PaymentMethodFee, payment.Divisibility).ToJObject());
        data.Add(payment is null
            ? null
            : DisplayFormatter.ToFormattedAmount(payment.InvoicePaidAmount.Gross, parentEntity.Currency));

        metadataFields.WriteValues(data);
        metadataFields.Values.Clear(); // We don't want to duplicate the data on all payments
    }

    class Context
    {
        public Context(InvoiceEntity invoiceEntity, DisplayFormatter displayFormatter)
        {
            Invoice = invoiceEntity;
            DisplayFormatter = displayFormatter;
        }

        public InvoiceEntity Invoice { get; }
        public DisplayFormatter DisplayFormatter { get; }

        public string? ItemName;
        public List<String> Path = new();

        public IDisposable Enter(string name)
        {
            var prev = ItemName;
            ItemName = name;
            Path.Add(name);
            return new ActionDisposable(() =>
            {
                ItemName = prev;
                Path.RemoveAt(Path.Count - 1);
            });
        }
    }

    private void FlattenFields(JToken obj, MetadataFields result, Context context)
    {
        if (context.Path
            // When we have this field to non-zero, then the invoice has a taxIncluded metadata
            is ["posData", "tax"]
            // Verbose data
            or ["itemDesc"])
            return;
        switch (obj)
        {
            case JObject o:
                foreach (var prop in o.Properties())
                {
                    if (context.ItemName is null)
                    {
                        // Those fields are already exported by default, or doesn't contain useful data.
                        if (prop.Name is "receiptData")
                            continue;
                    }

                    using var _ = context.Enter(prop.Name);
                    FlattenFields(prop.Value, result, context);
                }

                break;
            case JArray a:
                var isCart = context.Path is ["posData", "cart"];
                foreach (var item in a)
                {
                    if (isCart &&
                        item is JObject cartItem &&
                        cartItem.SelectToken("id")?.ToString() is string itemId)
                    {
                        using var _ = context.Enter(itemId);
                        result.HasCartItem(itemId);
                        FlattenFields(item, result, context);
                    }
                    else
                    {
                        FlattenFields(item, result, context);
                    }
                }

                break;
            case JValue { Value: { } v } jv when context.ItemName is not null:
                var fieldName = context.ItemName;
                if (context.Path is ["posData", "cart", { } itemId2, ..])
                {
                    if (fieldName is "id" or "image" or "title" or "inventory")
                        break;
                    try
                    {
                        if (fieldName == "price")
                            v = context.DisplayFormatter.ToFormattedAmount(Convert.ToDecimal(v, CultureInfo.InvariantCulture), context.Invoice.Currency);
                    }
                    catch (InvalidCastException) { }
                    fieldName = $"{itemId2}-{fieldName}";
                }
                result.TryAdd(fieldName, v);
                break;
        }
    }

    public InvoicesReportProvider(DisplayFormatter displayFormatter, InvoiceRepository invoiceRepository, StoreRepository storeRepository)
    {
        DisplayFormatter = displayFormatter;
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
    }
}
