using System;
using BTCPayServer.Data;


namespace BTCPayServer.Models.AppViewModels
{
    public class ListAppsViewModel
    {
        public class ListAppViewModel
        {
            public string Id { get; set; }
            public string StoreName { get; set; }
            public string StoreId { get; set; }
            public string AppName { get; set; }
            public string AppType { get; set; }
            public string ViewStyle { get; set; }
            public bool IsOwner { get; set; }

            public string UpdateAction { get { return "Update" + AppType; } }
            public string ViewAction { get { return "View" + AppType; } }
            public DateTimeOffset Created { get; set; }
            public AppData App { get; set; }
        }

        public ListAppViewModel[] Apps { get; set; }
    }
}
