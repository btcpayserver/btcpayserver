using System;
using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Views
{
    public static class ViewsRazor
    {
        private const string ACTIVE_CATEGORY_KEY = "ActiveCategory";
        private const string ACTIVE_PAGE_KEY = "ActivePage";

        public static void SetActivePageAndTitle<T>(this ViewDataDictionary viewData, T activePage, string title = null, string mainTitle = null)
            where T : IConvertible
        {
            // Browser Title
            viewData["Title"] = title ?? activePage.ToString();
            // Breadcrumb
            viewData["MainTitle"] = mainTitle;
            viewData["PageTitle"] = title;
            // Navigation
            viewData[ACTIVE_PAGE_KEY] = activePage;
            SetActiveCategory(viewData, activePage.GetType());
        }

        public static void SetActiveCategory<T>(this ViewDataDictionary viewData, T activeCategory)
        {
            viewData[ACTIVE_CATEGORY_KEY] = activeCategory;
        }

        public static string IsActiveCategory<T>(this ViewDataDictionary viewData, T category)
        {
            if (!viewData.ContainsKey(ACTIVE_CATEGORY_KEY))
            {
                return null;
            }
            var activeCategory = (T)viewData[ACTIVE_CATEGORY_KEY];
            return category.Equals(activeCategory) ? "active" : null;
        }

        public static string IsActivePage<T>(this ViewDataDictionary viewData, T page)
            where T : IConvertible
        {
            if (!viewData.ContainsKey(ACTIVE_PAGE_KEY))
            {
                return null;
            }
            var activePage = (T)viewData[ACTIVE_PAGE_KEY];
            return page.Equals(activePage) ? "active" : null;
        }

        public static HtmlString ToBrowserDate(this DateTimeOffset date)
        {
            var displayDate = date.ToString("o", CultureInfo.InvariantCulture);
            return new HtmlString($"<span class='localizeDate'>{displayDate}</span>");
        }

        public static HtmlString ToBrowserDate(this DateTime date)
        {
            var displayDate = date.ToString("o", CultureInfo.InvariantCulture);
            return new HtmlString($"<span class='localizeDate'>{displayDate}</span>");
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
