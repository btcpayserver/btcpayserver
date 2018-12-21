using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration
{
    public class SparkConnectionString
    {
        public Uri Server { get; private set; }
        public string CookeFile { get; private set; }

        public static bool TryParse(string str, out SparkConnectionString result)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            result = null;
            var resultTemp = new SparkConnectionString();
            foreach(var kv in str.Split(';')
                        .Select(part => part.Split('='))
                        .Where(kv => kv.Length == 2))
            {
                switch (kv[0].ToLowerInvariant())
                {
                    case "server":
                        if (resultTemp.Server != null)
                            return false;
                        if (!Uri.IsWellFormedUriString(kv[1], UriKind.Absolute))
                            return false;
                        resultTemp.Server = new Uri(kv[1], UriKind.Absolute);
                        break;
                    case "cookiefile":
                    case "cookiefilepath":
                        if (resultTemp.CookeFile != null)
                            return false;
                        resultTemp.CookeFile = kv[1];
                        break;
                    default:
                        return false;
                }
            }
            result = resultTemp;
            return true;
        }
    }
}
