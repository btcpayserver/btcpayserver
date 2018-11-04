using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;
using NBitcoin;
using System.Text;
using CommandLine;
using NBXplorer;

namespace BTCPayServer.Configuration
{
    public class DefaultConfiguration : StandardConfiguration.DefaultConfiguration
    {
        protected override CommandLineApplication CreateCommandLineApplicationCore()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
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
            app.Option("--chains | -c", $"Chains to support as a comma separated (default: btc; available: {chains})", CommandOptionType.SingleValue);
            app.Option("--postgres", $"Connection string to a PostgreSQL database (default: SQLite)", CommandOptionType.SingleValue);
            app.Option("--mysql", $"Connection string to a MySQL database (default: SQLite)", CommandOptionType.SingleValue);
            app.Option("--externalurl", $"The expected external URL of this service, to use if BTCPay is behind a reverse proxy (default: empty, use the incoming HTTP request to figure out)", CommandOptionType.SingleValue);
            app.Option("--bundlejscss", $"Bundle JavaScript and CSS files for better performance (default: true)", CommandOptionType.SingleValue);
            app.Option("--rootpath", "The root path in the URL to access BTCPay (default: /)", CommandOptionType.SingleValue);
            app.Option("--sshconnection", "SSH server to manage BTCPay under the form user@server:port (default: root@externalhost or empty)", CommandOptionType.SingleValue);
            app.Option("--sshpassword", "SSH password to manage BTCPay (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshkeyfile", "SSH private key file to manage BTCPay (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshkeyfilepassword", "Password of the SSH keyfile (default: empty)", CommandOptionType.SingleValue);
            app.Option("--sshtrustedfingerprints", "SSH Host public key fingerprint or sha256 (default: empty, it will allow untrusted connections)", CommandOptionType.SingleValue);
            app.Option("--debuglog", "A rolling log file for debug messages.", CommandOptionType.SingleValue);
            app.Option("--debugloglevel", "The severity you log (default:information)", CommandOptionType.SingleValue);
            foreach (var network in provider.GetAll())
            {
                var crypto = network.CryptoCode.ToLowerInvariant();
                app.Option($"--{crypto}explorerurl", $"URL of the NBXplorer for {network.CryptoCode} (default: {network.NBXplorerNetwork.DefaultSettings.DefaultUrl})", CommandOptionType.SingleValue);
                app.Option($"--{crypto}explorercookiefile", $"Path to the cookie file (default: {network.NBXplorerNetwork.DefaultSettings.DefaultCookieFile})", CommandOptionType.SingleValue);
                app.Option($"--{crypto}lightning", $"Easy configuration of lightning for the server administrator: Must be a UNIX socket of c-lightning (lightning-rpc) or URL to a charge server (default: empty)", CommandOptionType.SingleValue);
                app.Option($"--{crypto}externallndgrpc", $"The LND gRPC configuration BTCPay will expose to easily connect to the internal lnd wallet from Zap wallet (default: empty)", CommandOptionType.SingleValue);
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

        public static NetworkType GetNetworkType(IConfiguration conf)
        {
            var network = conf.GetOrDefault<string>("network", null);
            if (network != null)
            {
                var n = Network.GetNetwork(network);
                if (n == null)
                {
                    throw new ConfigException($"Invalid network parameter '{network}'");
                }
                return n.NetworkType;
            }
            var net = conf.GetOrDefault<bool>("regtest", false) ? NetworkType.Regtest :
                        conf.GetOrDefault<bool>("testnet", false) ? NetworkType.Testnet : NetworkType.Mainnet;

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
            builder.AppendLine("#mysql=User ID=root;Password=myPassword;Host=localhost;Port=3306;Database=myDataBase;");
            builder.AppendLine();
            builder.AppendLine("### NBXplorer settings ###");
            foreach (var n in new BTCPayNetworkProvider(networkType).GetAll())
            {
                builder.AppendLine($"#{n.CryptoCode}.explorer.url={n.NBXplorerNetwork.DefaultSettings.DefaultUrl}");
                builder.AppendLine($"#{n.CryptoCode}.explorer.cookiefile={ n.NBXplorerNetwork.DefaultSettings.DefaultCookieFile}");
                builder.AppendLine($"#{n.CryptoCode}.lightning=/root/.lightning/lightning-rpc");
                builder.AppendLine($"#{n.CryptoCode}.lightning=https://apitoken:API_TOKEN_SECRET@charge.example.com/");
            }
            return builder.ToString();
        }



        protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
        {
            return new IPEndPoint(IPAddress.Parse("127.0.0.1"), BTCPayDefaultSettings.GetDefaultSettings(GetNetworkType(conf)).DefaultPort);
        }
    }
}
