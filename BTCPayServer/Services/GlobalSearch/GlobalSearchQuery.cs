using System;

namespace BTCPayServer.Services.GlobalSearch
{
    public class GlobalSearchQuery
    {
        public string RawQuery { get; init; }
        public string MatchQuery { get; init; }
        public string TransactionSearch { get; init; }
        public (DateTimeOffset Start, DateTimeOffset End)? DateRange { get; init; }
    }
}
