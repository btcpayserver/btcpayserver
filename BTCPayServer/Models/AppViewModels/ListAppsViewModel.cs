using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            public bool IsOwner { get; set; }

            public string UpdateAction { get { return "Update" + AppType; } }
            public string ViewAction { get { return "View" + AppType; } }
        }

        public ListAppViewModel[] Apps { get; set; }
    }
}
