using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using BTCPayServer.Hosting;
using BTCPayServer.Logging;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.Bitcoin;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Configuration
{
    public class DefaultConfiguration : StandardConfiguration.DefaultConfiguration
    {
        protected override CommandLineApplication CreateCommandLineApplicationCore()
        {
            var provider = CreateBTCPayNetworkProvider(ChainName.Mainnet);
            var chains = string.Join(",", provider.GetAll().Select(n => n.CryptoCode.ToLowerInvariant()).ToArray());
            CommandLineApplication app = new CommandLineApplication(true)
            {
                FullName = "BTCPay\r\nOpen source, self-hosted payment processor.",
                Name = "BTCPay"
            };
            app.HelpOption("-? | -h | --help");
            app.Option("-n | --network", $"Set the network among (mainnet,testnet,regtest) (default: mainnet)", CommandOptionType.SingleValue);
            app.Option("--testnet | -testnet", $"Use testnet (deprecated, use --network instead)", CommandOptionType.BoolValue);
            app.Option("--regtest | -regtest", $"Use regtest (deprecated, use --network instead)", CommandOptionType.BoolValue);
            app.Option("--signet | -signet", $"Use signet (deprecated, use --network instead)", CommandOptionType.BoolValue);
            app.Option("--chains | -c", $"Chains to support as a comma separated (default: btc; available: {chains})", CommandOptionType.SingleValue);
            app.Option("--postgres", $"Connection string to a PostgreSQL database", CommandOptionType.SingleValue);
            app.Option("--nocsp", $"Disable CSP (default false)", CommandOptionType.BoolValue);
            app.Option("--deprecated", $"Allow deprecated settings (default:false)", CommandOptionType.BoolValue);
            app.Option("--externalservices", $"Links added to external services inside Server Settings / Services under the format service1:path2;service2:path2.(default: empty)", CommandOptionType.SingleValue);
            app.Option("--rootpath", "The root path in the URL to access BTCPay (default: /)", CommandOptionType.SingleValue);
            app.Option("--sshconnection", "SSH server to manage BTCPay under the form user@server:port (default: root@externalhost or empty)", CommandOptionType.SingleValue);
            app.Option("--sshpassword", "SSH password to manage BTCPay (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshkeyfile", "SSH private key file to manage BTCPay (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshkeyfilepassword", "Password of the SSH keyfile (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshauthorizedkeys", "Path to a authorized_keys file that BTCPayServer can modify from the website (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshtrustedfingerprints", "SSH Host public key fingerprint or sha256 (default: empty, it will allow untrusted connections)", CommandOptionType.SingleValue);
            app.Option("--torrcfile", "Path to torrc file containing hidden services directories (default: empty)", CommandOptionType.SingleValue);
            app.Option("--torservices", "Tor hostnames of available services added to Server Settings (and sets onion header for btcpay). Format: btcpayserver:host.onion:80;btc-p2p:host2.onion:81,BTC-RPC:host3.onion:82,UNKNOWN:host4.onion:83. (default: empty)", CommandOptionType.SingleValue);
            app.Option("--socksendpoint", "Socks endpoint to connect to onion urls (default: empty)", CommandOptionType.SingleValue);
            app.Option("--updateurl", $"Url used for once a day new release version check. Check performed only if value is not empty (default: empty)", CommandOptionType.SingleValue);
            app.Option("--debuglog", "A rolling log file for debug messages.", CommandOptionType.SingleValue);
            app.Option("--debugloglevel", "The severity you log (default:information)", CommandOptionType.SingleValue);
            app.Option("--disable-registration", "Disables new user registrations (default:true)", CommandOptionType.SingleValue);
            app.Option("--recommended-plugins", "Plugins which would be marked as recommended to be installed. Separated by newline or space", CommandOptionType.MultipleValue);
            app.Option("--xforwardedproto", "If specified, set X-Forwarded-Proto to the specified value, this may be useful if your reverse proxy handle https but is not configured to add X-Forwarded-Proto (example: --xforwardedproto https)", CommandOptionType.SingleValue);
            app.Option("--cheatmode", "Add some helper UI to facilitate dev-time testing (Default false)", CommandOptionType.BoolValue);

            app.Option("--explorerpostgres", $"Connection string to the postgres database of NBXplorer. (optional, used for dashboard and reporting features)", CommandOptionType.SingleValue);
            foreach (var network in provider.GetAll().OfType<BTCPayNetwork>())
            {
                var crypto = network.CryptoCode.ToLowerInvariant();
                app.Option($"--{crypto}explorerurl", $"URL of the NBXplorer for {network.CryptoCode} (default: {network.NBXplorerNetwork.DefaultSettings.DefaultUrl})", CommandOptionType.SingleValue);
                app.Option($"--{crypto}explorercookiefile", $"Path to the cookie file (default: {network.NBXplorerNetwork.DefaultSettings.DefaultCookieFile})", CommandOptionType.SingleValue);
                app.Option($"--{crypto}lightning", $"Easy configuration of lightning for the server administrator: Must be a UNIX socket of c-lightning (lightning-rpc) or URL to a charge server (default: empty)", CommandOptionType.SingleValue);
                if (network.SupportLightning)
                {
                    app.Option($"--{crypto}externallndgrpc", $"The LND gRPC configuration BTCPay will expose to easily connect to the internal lnd wallet from an external wallet (default: empty)", CommandOptionType.SingleValue);
                    app.Option($"--{crypto}externallndrest", $"The LND REST configuration BTCPay will expose to easily connect to the internal lnd wallet from an external wallet (default: empty)", CommandOptionType.SingleValue);
                    app.Option($"--{crypto}externalrtl", $"The Ride the Lightning configuration so BTCPay will expose to easily open it in server settings (default: empty)", CommandOptionType.SingleValue);
                    app.Option($"--{crypto}externalspark", $"Show spark information in Server settings / Server. The connection string to spark server (default: empty)", CommandOptionType.SingleValue);
                    app.Option($"--{crypto}externalcharge", $"Show lightning charge information in Server settings/Server. The connection string to charge server (default: empty)", CommandOptionType.SingleValue);
                }
            }
            return app;
        }

        public override string EnvironmentVariablePrefix => "BTCPAY_";

        protected override string GetDefaultDataDir(IConfiguration conf)
        {
            return BTCPayDefaultSettings.GetDefaultSettings(GetNetworkType(conf)).DefaultDataDirectory;
        }

        protected override string GetDefaultConfigurationFile(IConfiguration conf)
        {
            var network = BTCPayDefaultSettings.GetDefaultSettings(GetNetworkType(conf));
            var dataDir = conf["datadir"];
            if (dataDir == null)
                return network.DefaultConfigurationFile;
            var fileName = Path.GetFileName(network.DefaultConfigurationFile);
            var chainDir = Path.GetFileName(Path.GetDirectoryName(network.DefaultConfigurationFile));
            chainDir = Path.Combine(dataDir, chainDir);
            try
            {
                if (!Directory.Exists(chainDir))
                    Directory.CreateDirectory(chainDir);
            }
            catch { }
            return Path.Combine(chainDir, fileName);
        }

        public static ChainName GetNetworkType(IConfiguration conf)
        {
            var network = conf.GetOrDefault<string>("network", null);
            if (network != null)
            {
                var n = Network.GetNetwork(network);
                if (n == null)
                {
                    throw new ConfigException($"Invalid network parameter '{network}'");
                }
                return n.ChainName;
            }
            var net = conf.GetOrDefault<bool>("regtest", false) ? ChainName.Regtest :
                        conf.GetOrDefault<bool>("testnet", false) ? ChainName.Testnet :
                        conf.GetOrDefault<bool>("signet", false) ? Bitcoin.Instance.Signet.ChainName :
                        ChainName.Mainnet;

            return net;
        }

        protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
        {
            var networkType = GetNetworkType(conf);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### Global settings ###");
            builder.AppendLine("#network=mainnet");
            builder.AppendLine();
            builder.AppendLine("### Server settings ###");
            builder.AppendLine("#port=" + defaultSettings.DefaultPort);
            builder.AppendLine("#bind=127.0.0.1");
            builder.AppendLine("#httpscertificatefilepath=devtest.pfx");
            builder.AppendLine("#httpscertificatefilepassword=toto");
            builder.AppendLine();
            builder.AppendLine("### Database ###");
            builder.AppendLine("#postgres=User ID=root;Password=myPassword;Host=localhost;Port=5432;Database=myDataBase;");
            builder.AppendLine();
            builder.AppendLine("### NBXplorer settings ###");
            foreach (var n in CreateBTCPayNetworkProvider(networkType).GetAll().OfType<BTCPayNetwork>())
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"#{n.CryptoCode}.explorer.url={n.NBXplorerNetwork.DefaultSettings.DefaultUrl}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"#{n.CryptoCode}.explorer.cookiefile={n.NBXplorerNetwork.DefaultSettings.DefaultCookieFile}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"#{n.CryptoCode}.blockexplorerlink=https://mempool.space/tx/{{0}}");
                if (n.SupportLightning)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"#{n.CryptoCode}.lightning=/root/.lightning/lightning-rpc");
                    builder.AppendLine(CultureInfo.InvariantCulture, $"#{n.CryptoCode}.lightning=https://apitoken:API_TOKEN_SECRET@charge.example.com/");
                }
            }
            return builder.ToString();
        }

        private BTCPayNetworkProvider CreateBTCPayNetworkProvider(ChainName networkType)
        {
            var collection = new ServiceCollection();
            var conf = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("network", networkType.ToString())
            }).Build();
            var services = new PluginServiceCollection(collection, Startup.CreateBootstrap(conf));
            var p1 = new BitcoinPlugin();
            p1.Execute(services);

            var p2 = new Plugins.Altcoins.AltcoinsPlugin();
            p2.Execute(services);

            services.AddSingleton(services.BootstrapServices.GetRequiredService<SelectedChains>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<Logs>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<IConfiguration>());
            services.AddSingleton<BTCPayNetworkProvider>();
            return services.BuildServiceProvider().GetRequiredService<BTCPayNetworkProvider>();
        }
        protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
        {
            return new IPEndPoint(IPAddress.Parse("127.0.0.1"), BTCPayDefaultSettings.GetDefaultSettings(GetNetworkType(conf)).DefaultPort);
        }
    }
}
