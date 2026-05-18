using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Localization;

namespace BTCPayServer
{
    public static class InvoiceStateExtensions
    {
        public static string ToCssClass(this InvoiceState state)
        {
            return state.Status switch
            {
                InvoiceStatus.Expired when state.ExceptionStatus is InvoiceExceptionStatus.PaidLate or InvoiceExceptionStatus.PaidPartial or InvoiceExceptionStatus.PaidOver => "unusual",
                InvoiceStatus.Expired => "expired",
                _ => state.Status.ToString().ToLowerInvariant()
            };
        }

        public static string ToLocalizedString(this InvoiceState state, IStringLocalizer stringLocalizer)
        {
            var status = state.Status switch
            {
                InvoiceStatus.New => stringLocalizer["New"].Value,
                InvoiceStatus.Processing => stringLocalizer["Processing"].Value,
                InvoiceStatus.Expired => stringLocalizer["Expired"].Value,
                InvoiceStatus.Invalid => stringLocalizer["Invalid"].Value,
                InvoiceStatus.Settled => stringLocalizer["Settled"].Value,
                _ => state.Status.ToString()
            };

            return state.ExceptionStatus switch
            {
                InvoiceExceptionStatus.PaidOver => stringLocalizer["{0} (paid over)", status].Value,
                InvoiceExceptionStatus.PaidLate => stringLocalizer["{0} (paid late)", status].Value,
                InvoiceExceptionStatus.PaidPartial => stringLocalizer["{0} (paid partial)", status].Value,
                InvoiceExceptionStatus.Marked => stringLocalizer["{0} (marked)", status].Value,
                _ => status
            };
        }
    }
}
