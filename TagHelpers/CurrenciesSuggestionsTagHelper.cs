using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Razor.TagHelpers;

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
            var currencies = _currencies.Currencies
                .Where(c => !c.Crypto)
                .OrderBy(c => c.Code).ToList();
            // insert btc at the front
            output.PostElement.AppendHtml("<option value=\"BTC\">BTC - Bitcoin</option>");
            output.PostElement.AppendHtml("<option value=\"SATS\">SATS - Satoshi</option>");
            // move most often used currencies up
            int pos = 0;
            InsertAt(currencies, "USD", pos++);
            InsertAt(currencies, "EUR", pos++);
            InsertAt(currencies, "JPY", pos++);
            InsertAt(currencies, "CNY", pos++);
            // add options
            foreach (var c in currencies)
            {
                output.PostElement.AppendHtml($"<option value=\"{c.Code}\">{c.Code} - {c.Name}</option>");
            }
            output.PostElement.AppendHtml("</datalist>");
            output.Attributes.Add("list", "currency-selection-suggestion");
            base.Process(context, output);
        }

        private void InsertAt(List<CurrencyData> currencies, string code, int idx)
        {
            var curr = currencies.FirstOrDefault(c => c.Code == code);
            currencies.Remove(curr);
            currencies.Insert(idx, curr);
        }
    }
}
