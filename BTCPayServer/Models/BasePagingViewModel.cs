using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Models
{
    public abstract class BasePagingViewModel
    {
        public const int CountDefault = 50;

        public int Skip { get; set; } = 0;
        public int Count { get; set; } = CountDefault;
        public int? Total { get; set; }


        /// <summary>
        /// SearchTerm is the part of the SearchString that isn't shown explicitly
        /// in the search text input.
        /// </summary>
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string SearchTerm { get; set; }

        [BindingBehavior(BindingBehavior.Never)]
        [ValidateNever]
        public SearchString Search { get; set; }

        /// <summary>
        /// SearchText is the part of the SearchString that is shown explicitly in the search text input.
        /// </summary>
        public string SearchText { get; set; }
        public string FilterCommand { get; set; }
        [ModelBinder(BinderType = typeof(TimeZoneInfoModelBinder))]
        [ValidateNever]
        public TimeZoneInfo BrowserTimeZone { get; set; }

        public SearchString GetSearch(TimeZoneInfo tz)
        {
            tz ??= BrowserTimeZone ?? TimeZoneInfo.Utc;
            var search = SearchString.Combine([SearchTerm, SearchText], tz);
            if (FilterCommand is not null)
                RunFilterCommand(search);
            AddUIFilters(search);
            SearchTerm = search.ToString(SearchStringFormat.OnlyUIFilters);
            SearchText = search.ToString(SearchStringFormat.ExceptUIFilters);
            Search = search;
            return search;
        }

        protected virtual void AddUIFilters(SearchString search)
        {
        }

        protected virtual void RunFilterCommand(SearchString search)
        {
            if (FilterCommand.StartsWith("set:"))
            {
                var kv = FilterCommand.Substring("set:".Length).Split('=');
                if (kv.Length == 2)
                {
                    search.SetFilter(kv[0], kv[1], true);
                }
            }
            if (FilterCommand.StartsWith("unset:"))
            {
                var k = FilterCommand.Substring("unset:".Length);
                search.SetFilter(k);
            }

            if (FilterCommand is "reset")
            {
                search.Filters.Clear();
                search.TextSearch = "";
                return;
            }

            if (FilterCommand is "alltime")
            {
                search.Filters.Remove("daterange");
                search.Filters.Remove("startdate");
                search.Filters.Remove("enddate");
            }

            if (FilterCommand.StartsWith("set-daterange:"))
            {
                var dateRange = FilterCommand.Substring("set-daterange:".Length);
                if (SearchString.IsValidDateRange(dateRange))
                    search.SetDateRange(dateRange, true);
            }
        }

        public IActionResult Redirect(HttpRequest request)
        {
            var query = QueryHelpers.ParseQuery(request.QueryString.Value);

            var newQuery = query.ToDictionary(
                x => x.Key,
                x => x.Value,
                StringComparer.OrdinalIgnoreCase);

            newQuery.Remove("SearchTerm");
            newQuery.Remove("SearchText");

            if (!string.IsNullOrEmpty(SearchTerm))
                newQuery["SearchTerm"] = new StringValues(SearchTerm);
            if (!string.IsNullOrEmpty(SearchText))
                newQuery["SearchText"] = new StringValues(SearchText);
            newQuery.Remove("FilterCommand");

            var path = (request.PathBase + request.Path).ToString();
            var newUrl = QueryHelpers.AddQueryString(path, newQuery);
            return new LocalRedirectResult(newUrl);
        }

        public Dictionary<string, object> PaginationQuery { get; set; }

        [ValidateNever]
        [BindingBehavior(BindingBehavior.Never)]
        public abstract int CurrentPageCount { get; }
    }
}
