using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Models
{
    public class BasePagingViewModel
    {
        public int Skip { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }
        public string SearchTerm { get; set; }
        public int? TimezoneOffset { get; set; }
    }
}
