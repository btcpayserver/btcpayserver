using System.Linq;
using Xunit;

namespace BTCPayServer.Tests;

internal class CSVInvoicesTester(string text) : CSVTester(text)
{
    string invoice = "";
    int payment = 0;

    public CSVInvoicesTester ForInvoice(string invoice)
    {
        this.payment = 0;
        this.invoice = invoice;
        return this;
    }
    public CSVInvoicesTester SelectPayment(int payment)
    {
        this.payment = payment;
        return this;
    }
    public CSVInvoicesTester AssertCount(int count)
    {
        Assert.Equal(count, _lines
            .Count(l => l[_indexes["InvoiceId"]] == invoice));
        return this;
    }

    public CSVInvoicesTester AssertValues(params (string, string)[] values)
    {
        var payments = _lines
            .Where(l => l[_indexes["InvoiceId"]] == invoice)
            .ToArray();
        var line = payments[payment];
        foreach (var (key, value) in values)
        {
            Assert.Equal(value, line[_indexes[key]]);
        }
        return this;
    }

    public string GetPaymentId() => _lines
        .Where(l => l[_indexes["InvoiceId"]] == invoice)
        .Select(l => l[_indexes["PaymentId"]])
        .FirstOrDefault();
}
