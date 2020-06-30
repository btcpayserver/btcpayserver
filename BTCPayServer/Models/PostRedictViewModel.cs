using System.Collections.Generic;

namespace BTCPayServer.Models
{
    public class PostRedirectViewModel
    {
        public string AspAction { get; set; }
        public string AspController { get; set; }
        public List<KeyValuePair<string, string>> Parameters { get; set; } = new List<KeyValuePair<string, string>>();
    }
}
