using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning
{
    public enum LightningConnectionType
    {
        Charge,
        CLightning
    }
    public class LightningConnectionString
    {
        public static bool TryParse(string str, out LightningConnectionString connectionString)
        {
            return TryParse(str, out connectionString, out var error);
        }
        public static bool TryParse(string str, out LightningConnectionString connectionString, out string error)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (str.StartsWith('/'))
                str = "unix:" + str;
            var result = new LightningConnectionString();
            connectionString = null;
            error = null;

            Uri uri;
            if (!System.Uri.TryCreate(str, UriKind.Absolute, out uri))
            {
                error = "Invalid URL";
                return false;
            }

            var supportedDomains = new string[] { "unix", "tcp", "http", "https" };
            if (!supportedDomains.Contains(uri.Scheme))
            {
                var protocols = String.Join(",", supportedDomains);
                error = $"The url support the following protocols {protocols}";
                return false;
            }

            if (uri.Scheme == "unix")
            {
                str = uri.AbsoluteUri.Substring("unix:".Length);
                while (str.Length >= 1 && str[0] == '/')
                {
                    str = str.Substring(1);
                }
                uri = new Uri("unix://" + str, UriKind.Absolute);
            }

            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                var parts = uri.UserInfo.Split(':');
                if (string.IsNullOrEmpty(uri.UserInfo) || parts.Length != 2)
                {
                    error = "The url is missing user and password";
                    return false;
                }
                result.Username = parts[0];
                result.Password = parts[1];
            }
            else if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "The url should not have user information";
                return false;
            }
            result.BaseUri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
            connectionString = result;
            return true;
        }

        public LightningConnectionString()
        {

        }

        public string Username { get; set; }
        public string Password { get; set; }
        public Uri BaseUri { get; set; }

        public LightningConnectionType ConnectionType
        {
            get
            {
                return BaseUri.Scheme == "http" || BaseUri.Scheme == "https" ? LightningConnectionType.Charge 
                    : LightningConnectionType.CLightning;
            }
        }

        public Uri ToUri(bool withCredentials)
        {
            if (withCredentials)
            {
                return new UriBuilder(BaseUri) { UserName = Username ?? "", Password = Password ?? "" }.Uri;
            }
            else
            {
                return BaseUri;
            }
        }

        public override string ToString()
        {
            return ToUri(true).AbsoluteUri;
        }
    }
}
