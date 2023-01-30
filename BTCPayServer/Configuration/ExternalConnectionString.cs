using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Controllers;
using NBitcoin;

namespace BTCPayServer.Configuration
{
    public class ExternalConnectionString
    {
        public ExternalConnectionString()
        {

        }
        public ExternalConnectionString(Uri server)
        {
            Server = server;
        }
        public Uri Server { get; set; }
        public byte[] Macaroon { get; set; }
        public Macaroons Macaroons { get; set; }
        public string MacaroonFilePath { get; set; }
        public string CertificateThumbprint { get; set; }
        public string MacaroonDirectoryPath { get; set; }
        public string APIToken { get; set; }
        public string CookieFilePath { get; set; }
        public string AccessKey { get; set; }

        /// <summary>
        /// Return a connectionString which does not depends on external resources or information like relative path or file path
        /// </summary>
        /// <returns></returns>
        public async Task<ExternalConnectionString> Expand(Uri absoluteUrlBase, ExternalServiceTypes serviceType, ChainName network)
        {
            var connectionString = this.Clone();
            // Transform relative URI into absolute URI
            var serviceUri = connectionString.Server.IsAbsoluteUri ? connectionString.Server : ToRelative(absoluteUrlBase, connectionString.Server.ToString());
            var isSecure = network != ChainName.Mainnet ||
                       serviceUri.Scheme == "https" ||
                       serviceUri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase) ||
                       Extensions.IsLocalNetwork(serviceUri.DnsSafeHost);
            if (!isSecure)
            {
                throw new System.Security.SecurityException($"Insecure transport protocol to access this service, please use HTTPS or TOR");
            }
            connectionString.Server = serviceUri;

            if (serviceType == ExternalServiceTypes.LNDGRPC || serviceType == ExternalServiceTypes.LNDRest || serviceType == ExternalServiceTypes.CLightningRest)
            {
                // Read the MacaroonDirectory
                if (connectionString.MacaroonDirectoryPath != null)
                {
                    try
                    {
                        connectionString.Macaroons = await Macaroons.GetFromDirectoryAsync(connectionString.MacaroonDirectoryPath);
                        connectionString.MacaroonDirectoryPath = null;
                    }
                    catch (Exception ex)
                    {
                        throw new System.IO.DirectoryNotFoundException("Macaroon directory path not found", ex);
                    }
                }

                // Read the MacaroonFilePath
                if (connectionString.MacaroonFilePath != null)
                {
                    try
                    {
                        connectionString.Macaroon = await System.IO.File.ReadAllBytesAsync(connectionString.MacaroonFilePath);
                        connectionString.MacaroonFilePath = null;
                    }
                    catch (Exception ex)
                    {
                        throw new System.IO.FileNotFoundException("Macaroon not found", ex);
                    }
                }
            }

            if (new[] { ExternalServiceTypes.Charge, ExternalServiceTypes.RTL, ExternalServiceTypes.ThunderHub,
                ExternalServiceTypes.Spark, ExternalServiceTypes.Configurator, ExternalServiceTypes.Torq }.Contains(serviceType))
            {
                // Read access key from cookie file
                if (connectionString.CookieFilePath != null)
                {
                    bool isFake = connectionString.CookieFilePath == "fake"; // Hacks for testing
                    string cookieFileContent = isFake ? "fake" : null;
                    if (!isFake)
                    {
                        try
                        {
                            cookieFileContent = await System.IO.File.ReadAllTextAsync(connectionString.CookieFilePath);
                        }
                        catch (Exception ex)
                        {
                            throw new System.IO.FileNotFoundException("Cookie file path not found", ex);
                        }
                    }
                    connectionString.CookieFilePath = null;

                    if (serviceType == ExternalServiceTypes.RTL || serviceType == ExternalServiceTypes.Configurator ||
                        serviceType == ExternalServiceTypes.ThunderHub || serviceType == ExternalServiceTypes.Torq)
                    {
                        connectionString.AccessKey = cookieFileContent;
                    }
                    else if (serviceType == ExternalServiceTypes.Spark)
                    {
                        var cookie = (isFake ? "fake:fake:fake" : cookieFileContent).Split(':');
                        if (cookie.Length >= 3)
                        {
                            connectionString.AccessKey = cookie[2];
                        }
                        else
                        {
                            throw new FormatException("Invalid cookiefile format");
                        }
                    }
                    else if (serviceType == ExternalServiceTypes.Charge)
                    {
                        connectionString.APIToken = isFake ? "fake" : cookieFileContent;
                    }
                }
            }
            return connectionString;
        }

        private Uri ToRelative(Uri absoluteUrlBase, string path)
        {
            if (path.StartsWith('/'))
                path = path.Substring(1);
            return new Uri($"{absoluteUrlBase.AbsoluteUri.WithTrailingSlash()}{path}", UriKind.Absolute);
        }

        public ExternalConnectionString Clone()
        {
            return new ExternalConnectionString()
            {
                MacaroonFilePath = MacaroonFilePath,
                CertificateThumbprint = CertificateThumbprint,
                Macaroon = Macaroon,
                MacaroonDirectoryPath = MacaroonDirectoryPath,
                Server = Server,
                APIToken = APIToken,
                CookieFilePath = CookieFilePath,
                AccessKey = AccessKey,
                Macaroons = Macaroons?.Clone()
            };
        }
        public bool? IsOnion()
        {
            return Server?.IsOnion();
        }
        public static bool TryParse(string str, out ExternalConnectionString result, out string error)
        {
            ArgumentNullException.ThrowIfNull(str);
            error = null;
            result = null;
            var resultTemp = new ExternalConnectionString();
            foreach (var kv in str.Split(';')
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
                        if (!Uri.IsWellFormedUriString(kv[1], UriKind.RelativeOrAbsolute))
                        {
                            error = "Invalid URI";
                            return false;
                        }
                        resultTemp.Server = new Uri(kv[1], UriKind.RelativeOrAbsolute);
                        if (!resultTemp.Server.IsAbsoluteUri && (kv[1].Length == 0 || kv[1][0] != '/'))
                            resultTemp.Server = new Uri($"/{kv[1]}", UriKind.RelativeOrAbsolute);
                        break;
                    case "cookiefile":
                    case "cookiefilepath":
                        if (resultTemp.CookieFilePath != null)
                        {
                            error = "Duplicated cookiefile attribute";
                            return false;
                        }

                        resultTemp.CookieFilePath = kv[1];
                        break;
                    case "macaroondirectorypath":
                        resultTemp.MacaroonDirectoryPath = kv[1];
                        break;
                    case "certthumbprint":
                        resultTemp.CertificateThumbprint = kv[1];
                        break;
                    case "macaroonfilepath":
                        resultTemp.MacaroonFilePath = kv[1];
                        break;
                    case "api-token":
                        resultTemp.APIToken = kv[1];
                        break;
                    case "access-key":
                        resultTemp.AccessKey = kv[1];
                        break;
                }
            }
            result = resultTemp;
            return true;
        }
    }
}
