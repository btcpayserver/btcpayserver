using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Payments.Lightning
{
    public enum LightningConnectionType
    {
        Charge,
        CLightning,
        Lnd
    }
    public class LightningConnectionString
    {
        public LightningConnectionString() { }

        public string Username { get; set; }
        public string Password { get; set; }
        public Uri BaseUri { get; set; } // has no user and password or query string

        public LightningConnectionType ConnectionType { get; private set; }

        // Extract this to LndConnectionString class
        public string Macaroon { get; set; }
        public string Tls { get; set; }
        //

        public Uri UriWithCreds
        {
            get { return new UriBuilder(BaseUri) { UserName = Username ?? "", Password = Password ?? "" }.Uri; }
        }

        public Uri UriPlain
        {
            get { return BaseUri; }
        }

        public override string ToString()
        {
            return UriWithCreds.AbsoluteUri;
        }

        public string ToFullEditString()
        {
            var dict = new Dictionary<string,string>();
            if (!String.IsNullOrEmpty(Macaroon))
                dict.Add("macaroon", Macaroon);
            if (!String.IsNullOrEmpty(Tls))
                dict.Add("tls", Tls);

            if (dict.Count > 0)
            {
                var qs = dict.Select(a => $"{a.Key}={a.Value}");
                return UriWithCreds.AbsoluteUri + "?" + String.Join("&", qs);
}
            else
                return UriWithCreds.AbsoluteUri;
        }

        //
        public static bool TryParse(string str, out LightningConnectionString connectionString)
        {
            return TryParse(str, out connectionString, out var error);
        }

        public static bool TryParse(string str, out LightningConnectionString connectionString, out string error)
        {
            if (str.StartsWith('/'))
                str = "unix:" + str;
            connectionString = null;
            error = null;

            Uri uri;
            if (!Uri.TryCreate(str, UriKind.Absolute, out uri))
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

            var result = new LightningConnectionString();

            result.ConnectionType = uri.Scheme == "http" || uri.Scheme == "https" ?
                LightningConnectionType.Charge :
                LightningConnectionType.CLightning;

            // TODO: We need to redo the way Lightning connection strings are detected
            // now that we have Lnd and we can't depend on
            // http => Charge
            // / => CLightning
            if (uri.ToString().Contains("lnd:lnd@"))
                result.ConnectionType = LightningConnectionType.Lnd;

            if (result.ConnectionType == LightningConnectionType.Charge)
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
            else if (result.ConnectionType == LightningConnectionType.Lnd)
            {
                result.Username = "lnd";
                result.Password = "lnd";
            }
            else if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "The url should not have user information";
                return false;
            }

            if (result.ConnectionType == LightningConnectionType.Lnd)
            {
                var queryString = QueryHelpers.ParseNullableQuery(uri.Query);
                if (queryString != null)
                {
                    result.Macaroon = queryString.ContainsKey("macaroon") ? queryString["macaroon"] : StringValues.Empty;
                    result.Tls = queryString.ContainsKey("tls") ? queryString["tls"] : StringValues.Empty;
                }
            }

            var uriWithoutQuery = uri.AbsoluteUri.Split('?')[0];
            result.BaseUri = new UriBuilder(uriWithoutQuery) { UserName = "", Password = "" }.Uri;
            connectionString = result;
            return true;
        }
    }
}
