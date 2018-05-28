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

        public static string ToTimeAgo(this DateTimeOffset date)
        {
            var formatted = (DateTimeOffset.UtcNow - date).TimeString() + " ago";
            return formatted;
        }

        public static string TimeString(this TimeSpan timeSpan)
        {
            if (timeSpan.TotalMinutes < 1)
            {
                return $"{(int)timeSpan.TotalSeconds} second{Plural((int)timeSpan.TotalSeconds)}";
            }
            if (timeSpan.TotalHours < 1)
            {
                return $"{(int)timeSpan.TotalMinutes} minute{Plural((int)timeSpan.TotalMinutes)}";
            }
            if (timeSpan.Days < 1)
            {
                return $"{(int)timeSpan.TotalHours} hour{Plural((int)timeSpan.TotalHours)}";
            }
            return $"{(int)timeSpan.TotalDays} day{Plural((int)timeSpan.TotalDays)}";
        }

        private static string Plural(int totalDays)
        {
            return totalDays > 1 ? "s" : string.Empty;
        }
    }
}
