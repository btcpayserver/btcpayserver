using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public class StoreReportsResponse
    {
        public string ViewName { get; set; }
        public StoreReportResponse.Field[] Fields
        {
            get;
            set;
        }
    }
}
