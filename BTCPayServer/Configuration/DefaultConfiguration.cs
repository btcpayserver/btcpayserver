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
            var provider = new BTCPayNetworkProvider(ChainType.Main);
            var chains = string.Join(",", provider.GetAll().Select(n => n.CryptoCode.ToLowerInvariant()).ToArray());
            CommandLineApplication app = new CommandLineApplication(true)
            {
                FullName = "BTCPay\r\nOpen source, self-hosted payment processor.",
                Name = "BTCPay"
            };
            app.HelpOption("-? | -h | --help");
            app.Option("-n | --network", $"Set the network among (mainnet,testnet,regtest) (default: mainnet)", CommandOptionType.SingleValue);
            app.Option("--testnet | -testnet", $"Use testnet (Deprecated, use --network instead)", CommandOptionType.BoolValue);
            app.Option("--regtest | -regtest", $"Use regtest (Deprecated, use --network instead)", CommandOptionType.BoolValue);
            app.Option("--chains | -c", $"Chains to support comma separated (default: btc, available: {chains})", CommandOptionType.SingleValue);
            app.Option("--postgres", $"Connection string to postgres database (default: sqlite is used)", CommandOptionType.SingleValue);
            foreach (var network in provider.GetAll())
            {
                var crypto = network.CryptoCode.ToLowerInvariant();
                app.Option($"--{crypto}explorerurl", $"Url of the NBxplorer for {network.CryptoCode} (default: {network.NBXplorerNetwork.DefaultSettings.DefaultUrl})", CommandOptionType.SingleValue);
                app.Option($"--{crypto}explorercookiefile", $"Path to the cookie file (default: {network.NBXplorerNetwork.DefaultSettings.DefaultCookieFile})", CommandOptionType.SingleValue);
            }
            app.Option("--externalurl", $"The expected external url of this service, to use if BTCPay is behind a reverse proxy (default: empty, use the incoming HTTP request to figure out)", CommandOptionType.SingleValue);
            return app;
        }

        public override string EnvironmentVariablePrefix => "BTCPAY_";

        protected override string GetDefaultDataDir(IConfiguration conf)
        {
            return BTCPayDefaultSettings.GetDefaultSettings(GetChainType(conf)).DefaultDataDirectory;
        }

        protected override string GetDefaultConfigurationFile(IConfiguration conf)
        {
            var network = BTCPayDefaultSettings.GetDefaultSettings(GetChainType(conf));
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

        public static ChainType GetChainType(IConfiguration conf)
        {
            var network = conf.GetOrDefault<string>("network", null);
            if (network != null)
            {
                var n = Network.GetNetwork(network);
                if (n == null)
                {
                    throw new ConfigException($"Invalid network parameter '{network}'");
                }
                return n.ToChainType();
            }
            var net = conf.GetOrDefault<bool>("regtest", false) ? ChainType.Regtest :
                        conf.GetOrDefault<bool>("testnet", false) ? ChainType.Test : ChainType.Main;

            return net;
        }

        protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
        {
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(GetChainType(conf));
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("### Global settings ###");
            builder.AppendLine("#network=mainnet");
            builder.AppendLine();
            builder.AppendLine("### Server settings ###");
            builder.AppendLine("#port=" + defaultSettings.DefaultPort);
            builder.AppendLine("#bind=127.0.0.1");
            builder.AppendLine();
            builder.AppendLine("### Database ###");
            builder.AppendLine("#postgres=User ID=root;Password=myPassword;Host=localhost;Port=5432;Database=myDataBase;");
            builder.AppendLine();
            builder.AppendLine("### NBXplorer settings ###");
            foreach (var n in new BTCPayNetworkProvider(defaultSettings.ChainType).GetAll())
            {
                builder.AppendLine($"#{n.CryptoCode}.explorer.url={n.NBXplorerNetwork.DefaultSettings.DefaultUrl}");
                builder.AppendLine($"#{n.CryptoCode}.explorer.cookiefile={ n.NBXplorerNetwork.DefaultSettings.DefaultCookieFile}");
            }
            return builder.ToString();
        }



        protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
        {
            return new IPEndPoint(IPAddress.Parse("127.0.0.1"), BTCPayDefaultSettings.GetDefaultSettings(GetChainType(conf)).DefaultPort);
        }
    }
}
