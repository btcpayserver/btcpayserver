using System;
using System.Collections.Generic;
using System.Globalization;
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
            var splitted = str.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            Filters
                = splitted
                    .Select(t => t.Split(new char[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(kv => kv.Length == 2)
                    .Select(kv => new KeyValuePair<string, string>(kv[0].ToLowerInvariant().Trim(), kv[1]))
                    .ToMultiValueDictionary(o => o.Key, o => o.Value);

            var val = splitted.FirstOrDefault(a => a?.IndexOf(':', StringComparison.OrdinalIgnoreCase) == -1);
            if (val != null)
                TextSearch = val.Trim();
            else
                TextSearch = "";
        }

        public string TextSearch { get; private set; }

        public MultiValueDictionary<string, string> Filters { get; private set; }

        public override string ToString()
        {
            return _OriginalString;
        }

        internal string[] GetFilterArray(string key)
        {
            return Filters.ContainsKey(key) ? Filters[key].ToArray() : null;
        }

        internal bool? GetFilterBool(string key)
        {
            if (!Filters.ContainsKey(key))
                return null;

            return bool.TryParse(Filters[key].First(), out var r) ?
                r : (bool?)null;
        }

        internal DateTimeOffset? GetFilterDate(string key, int timezoneOffset)
        {
            if (!Filters.ContainsKey(key))
                return null;

            var val = Filters[key].First();
            var success = DateTimeOffset.TryParse(val, null as IFormatProvider, DateTimeStyles.AssumeUniversal, out var r);
            if (success)
            {
                r = r.AddMinutes(timezoneOffset);
                return r;
            }

            return null;
        }
    }
}
