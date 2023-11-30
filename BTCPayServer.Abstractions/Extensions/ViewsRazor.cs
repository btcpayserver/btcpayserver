using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Abstractions.Extensions
{
    public static class ViewsRazor
    {
        private const string ACTIVE_CATEGORY_KEY = "ActiveCategory";
        private const string ACTIVE_PAGE_KEY = "ActivePage";
        private const string ACTIVE_ID_KEY = "ActiveId";
        private const string ActivePageClass = "active";

        public enum DateDisplayFormat
        {
            Localized,
            Relative
        }

        public static void SetBlazorAllowed(this ViewDataDictionary viewData, bool allowed)
        {
            viewData["BlazorAllowed"] = allowed;
        }
        public static bool IsBlazorAllowed(this ViewDataDictionary viewData)
        {
            return viewData["BlazorAllowed"] is not false;
        }

        public static void SetActivePage<T>(this ViewDataDictionary viewData, T activePage, string title = null, string activeId = null)
            where T : IConvertible
        {
            SetActivePage(viewData, activePage.ToString(), activePage.GetType().ToString(), title, activeId);
        }

        public static void SetActivePage(this ViewDataDictionary viewData, string activePage, string category, string title = null, string activeId = null)
        {
            // Page Title
            viewData["Title"] = title ?? activePage;
            // Navigation
            viewData[ACTIVE_PAGE_KEY] = activePage;
            viewData[ACTIVE_ID_KEY] = activeId;
            SetActiveCategory(viewData, category);
        }

        public static void SetActiveCategory<T>(this ViewDataDictionary viewData, T activeCategory)
        {
            SetActiveCategory(viewData, activeCategory.ToString());
        }

        public static void SetActiveCategory(this ViewDataDictionary viewData, string activeCategory)
        {
            viewData[ACTIVE_CATEGORY_KEY] = activeCategory;
        }

        public static string IsActiveCategory<T>(this ViewDataDictionary viewData, T category, object id = null)
        {
            return IsActiveCategory(viewData, category.ToString(), id);
        }

        public static string IsActiveCategory(this ViewDataDictionary viewData, string category, object id = null)
        {
            if (!viewData.ContainsKey(ACTIVE_CATEGORY_KEY))
            {
                return null;
            }
            var activeId = viewData[ACTIVE_ID_KEY];
            var activeCategory = viewData[ACTIVE_CATEGORY_KEY]?.ToString();
            var categoryMatch = category.Equals(activeCategory, StringComparison.InvariantCultureIgnoreCase);
            var idMatch = id == null || activeId == null || id.Equals(activeId);
            return categoryMatch && idMatch ? ActivePageClass : null;
        }

        public static string IsActivePage<T>(this ViewDataDictionary viewData, T page, object id = null)
            where T : IConvertible
        {
            return IsActivePage(viewData, page.ToString(), page.GetType().ToString(), id);
        }

        public static string IsActivePage<T>(this ViewDataDictionary viewData, IEnumerable<T> pages, object id = null)
            where T : IConvertible
        {
            return pages.Any(page => IsActivePage(viewData, page.ToString(), page.GetType().ToString(), id) == ActivePageClass)
                ? ActivePageClass
                : null;
        }

        public static string IsActivePage(this ViewDataDictionary viewData, string page, string category, object id = null)
        {
            if (!viewData.ContainsKey(ACTIVE_PAGE_KEY))
            {
                return null;
            }
            var activeId = viewData[ACTIVE_ID_KEY];
            var activePage = viewData[ACTIVE_PAGE_KEY]?.ToString();
            var activeCategory = viewData[ACTIVE_CATEGORY_KEY]?.ToString();
            var categoryAndPageMatch = (category == null || activeCategory.Equals(category, StringComparison.InvariantCultureIgnoreCase)) && page.Equals(activePage, StringComparison.InvariantCultureIgnoreCase);
            var idMatch = id == null || activeId == null || id.Equals(activeId);
            return categoryAndPageMatch && idMatch ? ActivePageClass : null;
        }

        public static HtmlString ToBrowserDate(this DateTimeOffset date, DateDisplayFormat format = DateDisplayFormat.Localized)
        {
            var relative = date.ToTimeAgo();
            var initial = format.ToString().ToLower();
            var dateTime = date.ToString("o", CultureInfo.InvariantCulture);
            var displayDate = format == DateDisplayFormat.Relative ? relative : date.ToString("g", CultureInfo.InvariantCulture);
            return new HtmlString($"<time datetime=\"{dateTime}\" data-relative=\"{relative}\" data-initial=\"{initial}\">{displayDate}</time>");
        }

        public static HtmlString ToBrowserDate(this DateTime date, DateDisplayFormat format = DateDisplayFormat.Localized)
        {
            var relative = date.ToTimeAgo();
            var initial = format.ToString().ToLower();
            var dateTime = date.ToString("o", CultureInfo.InvariantCulture);
            var displayDate = format == DateDisplayFormat.Relative ? relative : date.ToString("g", CultureInfo.InvariantCulture);
            return new HtmlString($"<time datetime=\"{dateTime}\" data-relative=\"{relative}\" data-initial=\"{initial}\">{displayDate}</time>");
        }

        public static string ToTimeAgo(this DateTimeOffset date) => (DateTimeOffset.UtcNow - date).ToTimeAgo();

        public static string ToTimeAgo(this DateTime date) => (DateTimeOffset.UtcNow - date).ToTimeAgo();

        public static string ToTimeAgo(this TimeSpan diff) => diff.TotalSeconds > 0 ? $"{diff.TimeString()} ago" : $"in {diff.Negate().TimeString()}";

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
            return timeSpan.Days < 1
                ? $"{(int)timeSpan.TotalHours} hour{Plural((int)timeSpan.TotalHours)}"
                : $"{(int)timeSpan.TotalDays} day{Plural((int)timeSpan.TotalDays)}";
        }

        private static string Plural(int value)
        {
            return value == 1 ? string.Empty : "s";
        }
    }
}
