using System;
using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Abstractions.Extensions
{
    public static class ViewsRazor
    {
        private const string ACTIVE_CATEGORY_KEY = "ActiveCategory";
        private const string ACTIVE_PAGE_KEY = "ActivePage";
        private const string ACTIVE_ID_KEY = "ActiveId";

        public static void SetActivePage<T>(this ViewDataDictionary viewData, T activePage, string title = null, string activeId = null)
            where T : IConvertible
        {
            // Page Title
            viewData["Title"] = title ?? activePage.ToString();
            // Navigation
            viewData[ACTIVE_PAGE_KEY] = activePage;
            viewData[ACTIVE_ID_KEY] = activeId;
            SetActiveCategory(viewData, activePage.GetType());
        }

        public static void SetActiveCategory<T>(this ViewDataDictionary viewData, T activeCategory)
        {
            viewData[ACTIVE_CATEGORY_KEY] = activeCategory;
        }

        public static string IsActiveCategory<T>(this ViewDataDictionary viewData, T category, object id = null)
        {
            if (!viewData.ContainsKey(ACTIVE_CATEGORY_KEY))
            {
                return null;
            }
            var activeId = viewData[ACTIVE_ID_KEY];
            var activeCategory = (T)viewData[ACTIVE_CATEGORY_KEY];
            var categoryMatch = category.Equals(activeCategory);
            var idMatch = id == null || activeId == null || id.Equals(activeId);
            return categoryMatch && idMatch ? "active" : null;
        }

        public static string IsActivePage<T>(this ViewDataDictionary viewData, T page, object id = null)
            where T : IConvertible
        {
            if (!viewData.ContainsKey(ACTIVE_PAGE_KEY))
            {
                return null;
            }
            var activeId = viewData[ACTIVE_ID_KEY];
            var activePage = (T)viewData[ACTIVE_PAGE_KEY];
            var activeCategory = viewData[ACTIVE_CATEGORY_KEY];
            var categoryAndPageMatch = activeCategory.Equals(activePage.GetType()) && page.Equals(activePage);
            var idMatch = id == null || activeId == null || id.Equals(activeId);
            return categoryAndPageMatch && idMatch ? "active" : null;
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
            var diff = DateTimeOffset.UtcNow - date;
            var formatted = diff.Seconds > 0 
                ? $"{diff.TimeString()} ago"
                : $"in {diff.Negate().TimeString()}";
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
