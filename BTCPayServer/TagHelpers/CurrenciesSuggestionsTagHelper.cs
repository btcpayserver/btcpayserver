using BTCPayServer.Services.Rates;
using System.Linq;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Collections.Generic;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement("input", Attributes = "currency-selection")]
    public class CurrenciesSuggestionsTagHelper : TagHelper
    {
        private readonly CurrencyNameTable _currencies;

        public CurrenciesSuggestionsTagHelper(CurrencyNameTable currencies)
        {
            _currencies = currencies;
        }
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.RemoveAll("currency-selection");
            output.PostElement.AppendHtml("<datalist id=\"currency-selection-suggestion\">");
            var currencies = _currencies.Currencies.Where(c => !c.Crypto).Select(c => c.Code).OrderBy(c => c).ToList();
            int pos = 0;
            InsertAt(currencies, "BTC", pos++);
            InsertAt(currencies, "SATS", pos++);
            InsertAt(currencies, "USD", pos++);
            InsertAt(currencies, "EUR", pos++);
            InsertAt(currencies, "JPY", pos++);
            InsertAt(currencies, "CNY", pos++);
            foreach (var curr in currencies)
            {
                output.PostElement.AppendHtml($"<option value=\"{curr}\">");
            }
            output.PostElement.AppendHtml("</datalist>");
            output.Attributes.Add("list", "currency-selection-suggestion");
            base.Process(context, output);
        }

        private void InsertAt(List<string> currencies, string curr, int idx)
        {
            currencies.Remove(curr);
            currencies.Insert(idx, curr);
        }
    }
}
