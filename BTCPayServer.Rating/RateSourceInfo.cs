using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Rating
{
    public class RateSourceInfo
    {
        public RateSourceInfo(string id, string displayName, string url)
        {
            Id = id;
            DisplayName = displayName;
            Url = url;
        }
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Url { get; set; }
    }
}
