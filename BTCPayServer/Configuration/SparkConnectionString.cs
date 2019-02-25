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

        public static bool TryParse(string str, out SparkConnectionString result, out string error)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            error = null;
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
                        {
                            error = "Duplicated server attribute";
                            return false;
                        }
                        if (!Uri.IsWellFormedUriString(kv[1], UriKind.Absolute))
                        {
                            error = "Invalid URI";
                            return false;
                        }
                        resultTemp.Server = new Uri(kv[1], UriKind.Absolute);
                        if(resultTemp.Server.Scheme == "http")
                        {
                            error = "Insecure transport protocol (http)";
                            return false;
                        }
                        break;
                    case "cookiefile":
                    case "cookiefilepath":
                        if (resultTemp.CookeFile != null)
                        {
                            error = "Duplicated cookiefile attribute";
                            return false;
                        }
                            
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
