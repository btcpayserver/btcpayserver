using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Views
{
    public static class ViewsRazor
    {
        public const string ACTIVE_PAGE_KEY = "ActivePage";
        public static void SetActivePageAndTitle<T>(this ViewDataDictionary viewData, T activePage, string title = null)
            where T : IConvertible
        {
            viewData["Title"] = title ?? activePage.ToString();
            viewData[ACTIVE_PAGE_KEY] = activePage;
        }

        public static string IsActivePage<T>(this ViewDataDictionary viewData, T page)
            where T : IConvertible
        {
            var activePage = (T)viewData[ACTIVE_PAGE_KEY];
            return page.Equals(activePage) ? "active" : null;
        }

        public static HtmlString ToBrowserDate(this DateTimeOffset date)
        {
            var hello = date.ToString("o", CultureInfo.InvariantCulture);
            return new HtmlString($"<span class='localizeDate'>{hello}</span>");
        }
    }
}
