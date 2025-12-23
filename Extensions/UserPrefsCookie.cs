namespace BTCPayServer
{
    public class UserPrefsCookie
    {
        public ListQueryDataHolder InvoicesQuery { get; set; }
        public ListQueryDataHolder PaymentRequestsQuery { get; set; }
        public ListQueryDataHolder UsersQuery { get; set; }
        public ListQueryDataHolder PayoutsQuery { get; set; }
        public ListQueryDataHolder PullPaymentsQuery { get; set; }
        public string CurrentStoreId { get; set; }
    }

    public class ListQueryDataHolder
    {
        public ListQueryDataHolder() { }

        public ListQueryDataHolder(string searchTerm, int? timezoneOffset, int? count)
        {
            SearchTerm = searchTerm;
            TimezoneOffset = timezoneOffset;
            Count = count;
        }

        public int? TimezoneOffset { get; set; }
        public string SearchTerm { get; set; }
        public int? Count { get; set; }
    }
}
