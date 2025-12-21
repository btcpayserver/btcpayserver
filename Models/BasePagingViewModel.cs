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
        public int? TimezoneOffset { get; set; }
        public Dictionary<string, object> PaginationQuery { get; set; }

        public abstract int CurrentPageCount { get; }
    }
}
