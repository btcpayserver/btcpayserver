using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
        public string SearchText { get; set; }
        public string FilterCommand { get; set; }

        public SearchString GetSearch(TimeZoneInfo tz)
        {
            var search = SearchString.Combine([SearchTerm, SearchText], tz);
            search.RunFilterCommand(FilterCommand);
            SetSearch(search);
            return search;
        }

        public void SetSearch(SearchString search)
        {
            SearchTerm = search.ToString(SearchStringFormat.OnlyUIFilters);
            SearchText = search.ToString(SearchStringFormat.ExceptUIFilters);
        }

        public Dictionary<string, object> PaginationQuery { get; set; }

        public abstract int CurrentPageCount { get; }
    }
}
