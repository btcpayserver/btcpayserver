using System.Collections.Generic;

namespace BTCPayServer.Models
{
    public class PostRedirectViewModel
    {
        public string AspAction { get; set; }
        public string AspController { get; set; }
        public string FormUrl { get; set; }
        public bool AllowExternal { get; set; }

        public MultiValueDictionary<string, string> FormParameters { get; set; } = new();
        public Dictionary<string, string> RouteParameters { get; set; } = new();
    }
}
