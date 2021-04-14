using System;
using System.IO;
using System.Linq;
using System.Net;
using BTCPayServer.Logging;
using BTCPayServer.SSH;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Serilog.Events;

namespace BTCPayServer.Configuration
{
    public class BTCPayServerOptions
    {
        public ChainName NetworkType
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
        public EndPoint SocksEndpoint { get; set; }


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
                    logfile = Path.Combine(new DataDirectories().Configure(configuration).DataDir, logfile);
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
            
            Logs.Configuration.LogInformation("Network: " + NetworkType.ToString());

            if (conf.GetOrDefault<bool>("launchsettings", false) && NetworkType != ChainName.Regtest)
                throw new ConfigException($"You need to run BTCPayServer with the run.sh or run.ps1 script");

          
            
            BundleJsCss = conf.GetOrDefault<bool>("bundlejscss", true);
            DockerDeployment = conf.GetOrDefault<bool>("dockerdeployment", true);
            AllowAdminRegistration = conf.GetOrDefault<bool>("allow-admin-registration", false);

            TorrcFile = conf.GetOrDefault<string>("torrcfile", null);
            TorServices = conf.GetOrDefault<string>("torservices", null)
                ?.Split(new[] {';', ','}, StringSplitOptions.RemoveEmptyEntries);
            if (!string.IsNullOrEmpty(TorrcFile) && TorServices != null)
                throw new ConfigException($"torrcfile or torservices should be provided, but not both");

            var socksEndpointString = conf.GetOrDefault<string>("socksendpoint", null);
            if (!string.IsNullOrEmpty(socksEndpointString))
            {
                if (!Utils.TryParseEndpoint(socksEndpointString, 9050, out var endpoint))
                    throw new ConfigException("Invalid value for socksendpoint");
                SocksEndpoint = endpoint;
            }

            UpdateUrl = conf.GetOrDefault<Uri>("updateurl", null);

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
            PluginRemote = conf.GetOrDefault("plugin-remote", "btcpayserver/btcpayserver-plugins");
            RecommendedPlugins = conf.GetOrDefault("recommended-plugins", "").ToLowerInvariant().Split('\r','\n','\t',' ').Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        }

        public string PluginRemote { get; set; }
        public string[] RecommendedPlugins { get; set; }

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
        public bool DockerDeployment { get; set; }
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
        public string[] TorServices { get; set; }
        public Uri UpdateUrl { get; set; }
    }
}
