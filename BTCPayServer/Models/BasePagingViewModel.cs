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

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string SearchTerm { get; set; }

        [BindingBehavior(BindingBehavior.Never)]
        [ValidateNever]
        public SearchString Search { get; set; }

        public string SearchText { get; set; }
        public string FilterCommand { get; set; }

        public SearchString GetSearch(TimeZoneInfo tz)
        {
            var search = SearchString.Combine([SearchTerm, SearchText], tz);
            if (FilterCommand is not null)
                RunFilterCommand(search);
            SearchTerm = search.ToString(SearchStringFormat.OnlyUIFilters);
            SearchText = search.ToString(SearchStringFormat.ExceptUIFilters);
            Search = search;
            return search;
        }

        public virtual void RunFilterCommand(SearchString search)
        {
            if (FilterCommand is "reset")
            {
                search.Filters.Clear();
                search.TextSearch = "";
                return;
            }
            if (FilterCommand is "alltime" or "thismonth" or "lastmonth" or "last30d" or "thisquarter" or "yeartodate")
            {
                search.Filters.Remove("startdate");
                search.Filters.Remove("enddate");
                if (FilterCommand != "alltime")
                {
                    search.Filters.Add("startdate", FilterCommand);
                    if (FilterCommand == "lastmonth")
                        search.Filters.Add("enddate", FilterCommand);
                }
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

        public abstract int CurrentPageCount { get; }
    }
}
