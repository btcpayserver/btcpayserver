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

namespace BTCPayServer.Configuration
{
	public class DefaultConfiguration : StandardConfiguration.DefaultConfiguration
	{
		protected override CommandLineApplication CreateCommandLineApplicationCore()
		{
			CommandLineApplication app = new CommandLineApplication(true)
			{
				FullName = "NBXplorer\r\nLightweight block explorer for tracking HD wallets",
				Name = "NBXplorer"
			};
			app.HelpOption("-? | -h | --help");
			app.Option("-n | --network", $"Set the network among ({NetworkInformation.ToStringAll()}) (default: {Network.Main.ToString()})", CommandOptionType.SingleValue);
			app.Option("--testnet | -testnet", $"Use testnet", CommandOptionType.BoolValue);
			app.Option("--regtest | -regtest", $"Use regtest", CommandOptionType.BoolValue);
			app.Option("--requirehttps", $"Will redirect to https version of the website (default: false)", CommandOptionType.BoolValue);
			app.Option("--postgres", $"Connection string to postgres database (default: sqlite is used)", CommandOptionType.SingleValue);
			app.Option("--explorerurl", $"Url of the NBxplorer (default: : Default setting of NBXplorer for the network)", CommandOptionType.SingleValue);
			app.Option("--explorercookiefile", $"Path to the cookie file (default: Default setting of NBXplorer for the network)", CommandOptionType.SingleValue);

			return app;
		}

		public override string EnvironmentVariablePrefix => "BTCPAY_";

		protected override string GetDefaultDataDir(IConfiguration conf)
		{
			return GetNetwork(conf).DefaultDataDirectory;
		}

		protected override string GetDefaultConfigurationFile(IConfiguration conf)
		{
			var network = GetNetwork(conf);
			var dataDir = conf["datadir"];
			if(dataDir == null)
				return network.DefaultConfigurationFile;
			var fileName = Path.GetFileName(network.DefaultConfigurationFile);
			return Path.Combine(dataDir, fileName);
		}

		public static NetworkInformation GetNetwork(IConfiguration conf)
		{
			var network = conf.GetOrDefault<string>("network", null);
			if(network != null)
			{
				var info = NetworkInformation.GetNetworkByName(network);
				if(info == null)
					throw new ConfigException($"Invalid network name {network}");
				return info;
			}

			var net = conf.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
						conf.GetOrDefault<bool>("testnet", false) ? Network.TestNet : Network.Main;

			return NetworkInformation.GetNetworkByName(net.Name);
		}

		protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
		{
			var network = GetNetwork(conf);
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("### Global settings ###");
			builder.AppendLine("#testnet=0");
			builder.AppendLine("#regtest=0");
			builder.AppendLine();
			builder.AppendLine("### Server settings ###");
			builder.AppendLine("#requirehttps=0");
			builder.AppendLine("#port=" + network.DefaultPort);
			builder.AppendLine("#bind=127.0.0.1");
			builder.AppendLine();
			builder.AppendLine("### Database ###");
			builder.AppendLine("#postgres=User ID=root;Password=myPassword;Host=localhost;Port=5432;Database=myDataBase;");
			builder.AppendLine();
			builder.AppendLine("### NBXplorer settings ###");
			builder.AppendLine("#explorer.url=" + network.DefaultExplorerUrl.AbsoluteUri);
			builder.AppendLine("#explorer.cookiefile=" + network.DefaultExplorerCookieFile);
			return builder.ToString();
		}



		protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
		{
			return new IPEndPoint(IPAddress.Parse("127.0.0.1"), GetNetwork(conf).DefaultPort);
		}
	}
}
