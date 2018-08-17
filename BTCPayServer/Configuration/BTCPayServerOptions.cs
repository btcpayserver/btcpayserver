using BTCPayServer.Logging;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using StandardConfiguration;
using Microsoft.Extensions.Configuration;
using NBXplorer;
using BTCPayServer.Payments.Lightning;
using Renci.SshNet;
using NBitcoin.DataEncoders;
using BTCPayServer.SSH;

namespace BTCPayServer.Configuration
{
    public class NBXplorerConnectionSetting
    {
        public string CryptoCode { get; internal set; }
        public Uri ExplorerUri { get; internal set; }
        public string CookieFile { get; internal set; }
    }

    public class BTCPayServerOptions
    {
        public NetworkType NetworkType
        {
            get; set;
        }
        public string ConfigurationFile
        {
            get;
            private set;
        }
        public string DataDir
        {
            get;
            private set;
        }
        public List<IPEndPoint> Listen
        {
            get;
            set;
        }

        public List<NBXplorerConnectionSetting> NBXplorerConnectionSettings
        {
            get;
            set;
        } = new List<NBXplorerConnectionSetting>();

        public void LoadArgs(IConfiguration conf)
        {
            NetworkType = DefaultConfiguration.GetNetworkType(conf);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType);
            DataDir = conf.GetOrDefault<string>("datadir", defaultSettings.DefaultDataDirectory);
            Logs.Configuration.LogInformation("Network: " + NetworkType.ToString());

            var supportedChains = conf.GetOrDefault<string>("chains", "btc")
                                      .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(t => t.ToUpperInvariant());
            NetworkProvider = new BTCPayNetworkProvider(NetworkType).Filter(supportedChains.ToArray());
            foreach (var chain in supportedChains)
            {
                if (NetworkProvider.GetNetwork(chain) == null)
                    throw new ConfigException($"Invalid chains \"{chain}\"");
            }

            var validChains = new List<string>();
            foreach (var net in NetworkProvider.GetAll())
            {
                NBXplorerConnectionSetting setting = new NBXplorerConnectionSetting();
                setting.CryptoCode = net.CryptoCode;
                setting.ExplorerUri = conf.GetOrDefault<Uri>($"{net.CryptoCode}.explorer.url", net.NBXplorerNetwork.DefaultSettings.DefaultUrl);
                setting.CookieFile = conf.GetOrDefault<string>($"{net.CryptoCode}.explorer.cookiefile", net.NBXplorerNetwork.DefaultSettings.DefaultCookieFile);
                NBXplorerConnectionSettings.Add(setting);
                {
                    var lightning = conf.GetOrDefault<string>($"{net.CryptoCode}.lightning", string.Empty);
                    if (lightning.Length != 0)
                    {
                        if (!LightningConnectionString.TryParse(lightning, true, out var connectionString, out var error))
                        {
                            throw new ConfigException($"Invalid setting {net.CryptoCode}.lightning, " + Environment.NewLine +
                                $"If you have a lightning server use: 'type=clightning;server=/root/.lightning/lightning-rpc', " + Environment.NewLine +
                                $"If you have a lightning charge server: 'type=charge;server=https://charge.example.com;api-token=yourapitoken'" + Environment.NewLine +
                                $"If you have a lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                $"              lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                error);
                        }
                        if (connectionString.IsLegacy)
                        {
                            Logs.Configuration.LogWarning($"Setting {net.CryptoCode}.lightning will work but use an deprecated format, please replace it by '{connectionString.ToString()}'");
                        }
                        InternalLightningByCryptoCode.Add(net.CryptoCode, connectionString);
                    }
                }

                {
                    var lightning = conf.GetOrDefault<string>($"{net.CryptoCode}.external.lnd.grpc", string.Empty);
                    if (lightning.Length != 0)
                    {
                        if (!LightningConnectionString.TryParse(lightning, false, out var connectionString, out var error))
                        {
                            throw new ConfigException($"Invalid setting {net.CryptoCode}.external.lnd.grpc, " + Environment.NewLine +
                                $"lnd server: 'type=lnd-grpc;server=https://lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                $"lnd server: 'type=lnd-grpc;server=https://lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                error);
                        }
                        ExternalServicesByCryptoCode.Add(net.CryptoCode, new ExternalLNDGRPC(connectionString));
                    }
                }
            }

            Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));

            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            BundleJsCss = conf.GetOrDefault<bool>("bundlejscss", true);
            ExternalUrl = conf.GetOrDefault<Uri>("externalurl", null);

            var sshSettings = ParseSSHConfiguration(conf);
            if ((!string.IsNullOrEmpty(sshSettings.Password) || !string.IsNullOrEmpty(sshSettings.KeyFile)) && !string.IsNullOrEmpty(sshSettings.Server))
            {
                int waitTime = 0;
                while (!string.IsNullOrEmpty(sshSettings.KeyFile) && !File.Exists(sshSettings.KeyFile))
                {
                    if(waitTime++ < 5)
                        System.Threading.Thread.Sleep(1000);
                    else
                        throw new ConfigException($"sshkeyfile does not exist");
                }
                    
                if (sshSettings.Port > ushort.MaxValue ||
                   sshSettings.Port < ushort.MinValue)
                    throw new ConfigException($"ssh port is invalid");
                if (!string.IsNullOrEmpty(sshSettings.Password) && !string.IsNullOrEmpty(sshSettings.KeyFile))
                    throw new ConfigException($"sshpassword or sshkeyfile should be provided, but not both");
                try
                {
                    sshSettings.CreateConnectionInfo();
                }
                catch
                {
                    throw new ConfigException($"sshkeyfilepassword is invalid");
                }
                SSHSettings = sshSettings;
            }

            var fingerPrints = conf.GetOrDefault<string>("sshtrustedfingerprints", "");
            if (!string.IsNullOrEmpty(fingerPrints))
            {
                foreach (var fingerprint in fingerPrints.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!SSHFingerprint.TryParse(fingerprint, out var f))
                        throw new ConfigException($"Invalid ssh fingerprint format {fingerprint}");
                    TrustedFingerprints.Add(f);
                }
            }

            RootPath = conf.GetOrDefault<string>("rootpath", "/");
            if (!RootPath.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
                RootPath = "/" + RootPath;
            var old = conf.GetOrDefault<Uri>("internallightningnode", null);
            if (old != null)
                throw new ConfigException($"internallightningnode should not be used anymore, use btclightning instead");
        }

        private SSHSettings ParseSSHConfiguration(IConfiguration conf)
        {
            var externalUrl = conf.GetOrDefault<Uri>("externalurl", null);
            var settings = new SSHSettings();
            settings.Server = conf.GetOrDefault<string>("sshconnection", null);
            if (settings.Server != null)
            {
                var parts = settings.Server.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    settings.Port = port;
                    settings.Server = parts[0];
                }
                else
                {
                    settings.Port = 22;
                }

                parts = settings.Server.Split('@');
                if (parts.Length == 2)
                {
                    settings.Username = parts[0];
                    settings.Server = parts[1];
                }
                else
                {
                    settings.Username = "root";
                }
            }
            else if (externalUrl != null)
            {
                settings.Port = 22;
                settings.Username = "root";
                settings.Server = externalUrl.DnsSafeHost;
            }
            settings.Password = conf.GetOrDefault<string>("sshpassword", "");
            settings.KeyFile = conf.GetOrDefault<string>("sshkeyfile", "");
            settings.KeyFilePassword = conf.GetOrDefault<string>("sshkeyfilepassword", "");
            return settings;
        }

        internal bool IsTrustedFingerprint(byte[] fingerPrint, byte[] hostKey)
        {
            return TrustedFingerprints.Any(f => f.Match(fingerPrint, hostKey));
        }

        public string RootPath { get; set; }
        public Dictionary<string, LightningConnectionString> InternalLightningByCryptoCode { get; set; } = new Dictionary<string, LightningConnectionString>();
        public ExternalServices ExternalServicesByCryptoCode { get; set; } = new ExternalServices();

        public BTCPayNetworkProvider NetworkProvider { get; set; }
        public string PostgresConnectionString
        {
            get;
            set;
        }
        public Uri ExternalUrl
        {
            get;
            set;
        }
        public bool BundleJsCss
        {
            get;
            set;
        }
        public List<SSHFingerprint> TrustedFingerprints { get; set; } = new List<SSHFingerprint>();
        public SSHSettings SSHSettings
        {
            get;
            set;
        }

        internal string GetRootUri()
        {
            if (ExternalUrl == null)
                return null;
            UriBuilder builder = new UriBuilder(ExternalUrl);
            builder.Path = RootPath;
            return builder.ToString();
        }
    }

    public class ExternalServices : MultiValueDictionary<string, ExternalService>
    {
        public IEnumerable<T> GetServices<T>(string cryptoCode) where T : ExternalService
        {
            if (!this.TryGetValue(cryptoCode.ToUpperInvariant(), out var services))
                return Array.Empty<T>();
            return services.OfType<T>();
        }
    }

    public class ExternalService
    {

    }

    public class ExternalLNDGRPC : ExternalService
    {
        public ExternalLNDGRPC(LightningConnectionString connectionString)
        {
            ConnectionString = connectionString;
        }

        public LightningConnectionString ConnectionString { get; set; }
    }
}
