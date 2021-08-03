#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTCPayServer.Common
{
    public class PaymentUrlBuilder
    {
        public PaymentUrlBuilder(string uriScheme)
        {
            UriScheme = uriScheme;
        }
        public string UriScheme { get; set; }
        public Dictionary<string, string> QueryParams { get; set; } = new Dictionary<string, string>();
        public string? Host { get; set; }
        public override string ToString()
        {
            StringBuilder builder = new StringBuilder($"{UriScheme}:{Host}");
            if (QueryParams.Count != 0)
            {
                var parts = QueryParams.Select(q => Uri.EscapeDataString(q.Key) + "=" + System.Web.NBitcoin.HttpUtility.UrlEncode(q.Value))
                    .ToArray();
                builder.Append($"?{string.Join('&', parts)}");
            }
            return builder.ToString();
        }
    }
}
