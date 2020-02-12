using BTCPayServer.Logging;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;
using BTCPayServer.SSH;
using BTCPayServer.Lightning;
using Serilog.Events;

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

        public string LogFile
        {
            get;
            private set;
        }
        public string DataDir
        {
            get;
            private set;
        }
        public EndPoint SocksEndpoint { get; set; }
        
        public List<NBXplorerConnectionSetting> NBXplorerConnectionSettings
        {
            get;
            set;
        } = new List<NBXplorerConnectionSetting>();

        public bool DisableRegistration
        {
            get;
            private set;
        }

        public static string GetDebugLog(IConfiguration configuration)
        {
            var logfile = configuration.GetValue<string>("debuglog", null);
            if (!string.IsNullOrEmpty(logfile))
            {
                if (!Path.IsPathRooted(logfile))
                {
                    var networkType = DefaultConfiguration.GetNetworkType(configuration);
                    logfile = Path.Combine(configuration.GetDataDir(networkType), logfile);
                }
            }
            return logfile;
        }
        public static LogEventLevel GetDebugLogLevel(IConfiguration configuration)
        {
            var raw = configuration.GetValue("debugloglevel", nameof(LogEventLevel.Debug));
            return (LogEventLevel)Enum.Parse(typeof(LogEventLevel), raw, true);
        }

        public void LoadArgs(IConfiguration conf)
        {
            NetworkType = DefaultConfiguration.GetNetworkType(conf);
            DataDir = conf.GetDataDir(NetworkType);
            Logs.Configuration.LogInformation("Network: " + NetworkType.ToString());

            if (conf.GetOrDefault<bool>("launchsettings", false) && NetworkType != NetworkType.Regtest)
                throw new ConfigException($"You need to run BTCPayServer with the run.sh or run.ps1 script");

            var supportedChains = conf.GetOrDefault<string>("chains", "btc")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant()).ToHashSet();

            var networkProvider = new BTCPayNetworkProvider(NetworkType);
            var filtered = networkProvider.Filter(supportedChains.ToArray());
            var elementsBased = filtered.GetAll().OfType<ElementsBTCPayNetwork>();
            var parentChains = elementsBased.Select(network => network.NetworkCryptoCode.ToUpperInvariant()).Distinct();
            var allSubChains = networkProvider.GetAll().OfType<ElementsBTCPayNetwork>()
                .Where(network => parentChains.Contains(network.NetworkCryptoCode)).Select(network => network.CryptoCode.ToUpperInvariant());
            supportedChains.AddRange(allSubChains);
            NetworkProvider = networkProvider.Filter(supportedChains.ToArray());
            foreach (var chain in supportedChains)
            {
                if (NetworkProvider.GetNetwork<BTCPayNetworkBase>(chain) == null)
                    throw new ConfigException($"Invalid chains \"{chain}\"");
            }

            var validChains = new List<string>();
            foreach (var net in NetworkProvider.GetAll().OfType<BTCPayNetwork>())
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
                            Logs.Configuration.LogWarning($"Invalid setting {net.CryptoCode}.lightning, " + Environment.NewLine +
                                $"If you have a c-lightning server use: 'type=clightning;server=/root/.lightning/lightning-rpc', " + Environment.NewLine +
                                $"If you have a lightning charge server: 'type=charge;server=https://charge.example.com;api-token=yourapitoken'" + Environment.NewLine +
                                $"If you have a lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                $"              lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                $"If you have an eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393;bitcoin-auth=bitcoinrpcuser:bitcoinrpcpassword" + Environment.NewLine +
                                $"               eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393" + Environment.NewLine +
                                $"Error: {error}" + Environment.NewLine +
                                "This service will not be exposed through BTCPay Server");
                        }
                        else
                        {
                            if (connectionString.IsLegacy)
                            {
                                Logs.Configuration.LogWarning($"Setting {net.CryptoCode}.lightning is a deprecated format, it will work now, but please replace it for future versions with '{connectionString.ToString()}'");
                            }
                            InternalLightningByCryptoCode.Add(net.CryptoCode, connectionString);
                        }
                    }
                }

                ExternalServices.Load(net.CryptoCode, conf);
            }

            ExternalServices.LoadNonCryptoServices(conf);
            Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));

            var services = conf.GetOrDefault<string>("externalservices", null);
            if (services != null)
            {
                foreach (var service in services.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(p => (p, SeparatorIndex: p.IndexOf(':', StringComparison.OrdinalIgnoreCase)))
                                                .Where(p => p.SeparatorIndex != -1)
                                                .Select(p => (Name: p.p.Substring(0, p.SeparatorIndex), 
                                                              Link: p.p.Substring(p.SeparatorIndex + 1))))
                {
                    if (Uri.TryCreate(service.Link, UriKind.RelativeOrAbsolute, out var uri))
                        OtherExternalServices.AddOrReplace(service.Name, uri);
                }
            }

            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            MySQLConnectionString = conf.GetOrDefault<string>("mysql", null);
            BundleJsCss = conf.GetOrDefault<bool>("bundlejscss", true);
            AllowAdminRegistration = conf.GetOrDefault<bool>("allow-admin-registration", false);
            TorrcFile = conf.GetOrDefault<string>("torrcfile", null);

            var socksEndpointString = conf.GetOrDefault<string>("socksendpoint", null);
            if(!string.IsNullOrEmpty(socksEndpointString))
            {
                if (!Utils.TryParseEndpoint(socksEndpointString, 9050, out var endpoint))
                    throw new ConfigException("Invalid value for socksendpoint");
                SocksEndpoint = endpoint;
            }
            

            var sshSettings = ParseSSHConfiguration(conf);
            if ((!string.IsNullOrEmpty(sshSettings.Password) || !string.IsNullOrEmpty(sshSettings.KeyFile)) && !string.IsNullOrEmpty(sshSettings.Server))
            {
                int waitTime = 0;
                while (!string.IsNullOrEmpty(sshSettings.KeyFile) && !File.Exists(sshSettings.KeyFile))
                {
                    if (waitTime++ < 5)
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
                    SSHSettings = sshSettings;
                }
                catch (NotSupportedException ex)
                {
                    Logs.Configuration.LogWarning($"The SSH key is not supported ({ex.Message}), try to generate the key with ssh-keygen using \"-m PEM\". Skipping SSH configuration...");
                }
                catch
                {
                    throw new ConfigException($"sshkeyfilepassword is invalid");
                }
            }

            var fingerPrints = conf.GetOrDefault<string>("sshtrustedfingerprints", "");
            if (!string.IsNullOrEmpty(fingerPrints))
            {
                foreach (var fingerprint in fingerPrints.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!SSHFingerprint.TryParse(fingerprint, out var f))
                        throw new ConfigException($"Invalid ssh fingerprint format {fingerprint}");
                    SSHSettings?.TrustedFingerprints.Add(f);
                }
            }

            RootPath = conf.GetOrDefault<string>("rootpath", "/");
            if (!RootPath.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
                RootPath = "/" + RootPath;
            var old = conf.GetOrDefault<Uri>("internallightningnode", null);
            if (old != null)
                throw new ConfigException($"internallightningnode is deprecated and should not be used anymore, use btclightning instead");

            LogFile = GetDebugLog(conf);
            if (!string.IsNullOrEmpty(LogFile))
            {
                Logs.Configuration.LogInformation("LogFile: " + LogFile);
                Logs.Configuration.LogInformation("Log Level: " + GetDebugLogLevel(conf));
            }

            DisableRegistration = conf.GetOrDefault<bool>("disable-registration", true);
        }

        private SSHSettings ParseSSHConfiguration(IConfiguration conf)
        {
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
            settings.Password = conf.GetOrDefault<string>("sshpassword", "");
            settings.KeyFile = conf.GetOrDefault<string>("sshkeyfile", "");
            settings.AuthorizedKeysFile = conf.GetOrDefault<string>("sshauthorizedkeys", "");
            settings.KeyFilePassword = conf.GetOrDefault<string>("sshkeyfilepassword", "");
            return settings;
        }

        public string RootPath { get; set; }
        public Dictionary<string, LightningConnectionString> InternalLightningByCryptoCode { get; set; } = new Dictionary<string, LightningConnectionString>();

        public Dictionary<string, Uri> OtherExternalServices { get; set; } = new Dictionary<string, Uri>();
        public ExternalServices ExternalServices { get; set; } = new ExternalServices();

        public BTCPayNetworkProvider NetworkProvider { get; set; }
        public string PostgresConnectionString
        {
            get;
            set;
        }
        public string MySQLConnectionString
        {
            get;
            set;
        }
        public bool BundleJsCss
        {
            get;
            set;
        }
        public bool AllowAdminRegistration { get; set; }
        public SSHSettings SSHSettings
        {
            get;
            set;
        }
        public string TorrcFile { get; set; }
    }
}
