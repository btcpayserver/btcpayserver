using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models
{
    public class PostRedirectViewModel
    {
        public string AspAction { get; set; }
        public string AspController { get; set; }
        public List<KeyValuePair<string,string>> Parameters { get; set; } = new List<KeyValuePair<string, string>>();
    }
}
