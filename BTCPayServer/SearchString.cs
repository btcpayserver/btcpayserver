using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public class SearchString
    {
        string _OriginalString;
        public SearchString(string str)
        {
            str = str ?? string.Empty;
            str = str.Trim();
            _OriginalString = str.Trim();
            TextSearch = _OriginalString;
            var splitted = str.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Filters
                = splitted
                    .Select(t => t.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(kv => kv.Length == 2)
                    .Select(kv => new KeyValuePair<string, string>(kv[0].ToLowerInvariant(), kv[1]))
                    .ToMultiValueDictionary(o => o.Key, o => o.Value);

            foreach(var filter in splitted)
            {
                if(filter.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries).Length == 2)
                { 
                    TextSearch = TextSearch.Replace(filter, string.Empty, StringComparison.InvariantCulture);
                }
            }
            TextSearch = TextSearch.Trim();
        }

        public string TextSearch
        {
            get;
            private set;
        }
        
        public MultiValueDictionary<string, string> Filters { get; private set; }

        public override string ToString()
        {
            return _OriginalString;
        }
    }
}
