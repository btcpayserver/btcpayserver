using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers.Logic
{
    public class ListInvoicesPreference
    {
        public const string KEY = "ListInvoicePreferences";
        public ListInvoicesPreference() { }

        public ListInvoicesPreference(string searchTerm, int timezoneOffset)
        {
            SearchTerm = searchTerm;
            if (timezoneOffset != 0)
                TimezoneOffset = timezoneOffset;
        }

        public int? TimezoneOffset { get; set; }
        public string SearchTerm { get; set; }
    }
}
