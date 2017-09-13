using BTCPayServer.Logging;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace BTCPayServer.Configuration
{
	public class BTCPayServerOptions
	{
		public Network Network
		{
			get; set;
		}
		public Uri Explorer
		{
			get; set;
		}

		public string CookieFile
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

		public void LoadArgs(TextFileConfiguration consoleConfig)
		{
			ConfigurationFile = consoleConfig.GetOrDefault<string>("conf", null);
			DataDir = consoleConfig.GetOrDefault<string>("datadir", null);
			if(DataDir != null && ConfigurationFile != null)
			{
				var isRelativePath = Path.GetFullPath(ConfigurationFile).Length > ConfigurationFile.Length;
				if(isRelativePath)
				{
					ConfigurationFile = Path.Combine(DataDir, ConfigurationFile);
				}
			}

			Network = consoleConfig.GetOrDefault<bool>("testnet", false) ? Network.TestNet :
				consoleConfig.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
				null;

			if(DataDir != null && ConfigurationFile == null)
			{
				ConfigurationFile = GetDefaultConfigurationFile(Network != null);
			}

			if(ConfigurationFile != null)
			{
				AssetConfigFileExists();
				var configTemp = TextFileConfiguration.Parse(File.ReadAllText(ConfigurationFile));
				Network = Network ?? (configTemp.GetOrDefault<bool>("testnet", false) ? Network.TestNet :
						  configTemp.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
						  null);
			}

			Network = Network ?? Network.Main;
			if(DataDir == null)
			{
				DataDir = DefaultDataDirectory.GetDefaultDirectory("BTCPayServer", Network, true);
				ConfigurationFile = GetDefaultConfigurationFile(true);
			}

			if(!Directory.Exists(DataDir))
				throw new ConfigurationException("Data directory does not exists");

			var config = TextFileConfiguration.Parse(File.ReadAllText(ConfigurationFile));
			consoleConfig.MergeInto(config, true);

			Logs.Configuration.LogInformation("Network: " + Network);
			Logs.Configuration.LogInformation("Data directory set to " + DataDir);
			Logs.Configuration.LogInformation("Configuration file set to " + ConfigurationFile);

			var defaultPort = config.GetOrDefault<int>("port", GetDefaultPort(Network));
			Listen = config
						.GetAll("bind")
						.Select(p => ConvertToEndpoint(p, defaultPort))
						.ToList();
			if(Listen.Count == 0)
			{
				Listen.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), defaultPort));
			}

			Explorer = config.GetOrDefault<Uri>("explorer.url", GetDefaultNXplorerUri());
			CookieFile = config.GetOrDefault<string>("explorer.cookiefile", GetExplorerDefaultCookiePath());
			ExternalUrl = config.GetOrDefault<Uri>("externalurl", null);
			if(ExternalUrl == null)
			{
				var ip = Listen.Where(u => !u.Address.ToString().Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)).FirstOrDefault() 
						?? new IPEndPoint(IPAddress.Parse("127.0.0.1"), defaultPort);
				ExternalUrl = new Uri($"http://{ip.Address}:{ip.Port}/");
			}
		}

		public Uri ExternalUrl
		{
			get; set;
		}

		private Uri GetDefaultNXplorerUri()
		{
			return new Uri("http://localhost:" + GetNXplorerDefaultPort(Network));
		}


		public string[] GetUrls()
		{
			return Listen.Select(b => "http://" + b + "/").ToArray();
		}

		private void AssetConfigFileExists()
		{
			if(!File.Exists(ConfigurationFile))
				throw new ConfigurationException("Configuration file does not exists");
		}

		public static IPEndPoint ConvertToEndpoint(string str, int defaultPort)
		{
			var portOut = defaultPort;
			var hostOut = "";
			int colon = str.LastIndexOf(':');
			// if a : is found, and it either follows a [...], or no other : is in the string, treat it as port separator
			bool fHaveColon = colon != -1;
			bool fBracketed = fHaveColon && (str[0] == '[' && str[colon - 1] == ']'); // if there is a colon, and in[0]=='[', colon is not 0, so in[colon-1] is safe
			bool fMultiColon = fHaveColon && (str.LastIndexOf(':', colon - 1) != -1);
			if(fHaveColon && (colon == 0 || fBracketed || !fMultiColon))
			{
				int n;
				if(int.TryParse(str.Substring(colon + 1), out n) && n > 0 && n < 0x10000)
				{
					str = str.Substring(0, colon);
					portOut = n;
				}
			}
			if(str.Length > 0 && str[0] == '[' && str[str.Length - 1] == ']')
				hostOut = str.Substring(1, str.Length - 2);
			else
				hostOut = str;
			return new IPEndPoint(IPAddress.Parse(hostOut), portOut);
		}

		const string DefaultConfigFile = "settings.config";
		private string GetDefaultConfigurationFile(bool createIfNotExist)
		{
			var config = Path.Combine(DataDir, DefaultConfigFile);
			Logs.Configuration.LogInformation("Configuration file set to " + config);
			if(createIfNotExist && !File.Exists(config))
			{
				Logs.Configuration.LogInformation("Creating configuration file");
				StringBuilder builder = new StringBuilder();
				builder.AppendLine("### Global settings ###");
				builder.AppendLine("#testnet=0");
				builder.AppendLine("#regtest=0");
				builder.AppendLine("#Put here the xpub key of your hardware wallet");
				builder.AppendLine("#hdpubkey=xpub...");
				builder.AppendLine();
				builder.AppendLine("### Server settings ###");
				builder.AppendLine("#port=" + GetDefaultPort(Network));
				builder.AppendLine("#bind=127.0.0.1");
				builder.AppendLine("#externalurl=http://127.0.0.1/");
				builder.AppendLine();
				builder.AppendLine("### NBXplorer settings ###");
				builder.AppendLine("#explorer.url=" + GetDefaultNXplorerUri());
				builder.AppendLine("#explorer.cookiefile=" + GetExplorerDefaultCookiePath());
				File.WriteAllText(config, builder.ToString());
			}
			return config;
		}

		private string GetExplorerDefaultCookiePath()
		{
			return Path.Combine(DefaultDataDirectory.GetDefaultDirectory("NBXplorer", Network, false), ".cookie");
		}

		private int GetNXplorerDefaultPort(Network network)
		{
			return network == Network.Main ? 24444 :
				network == Network.TestNet ? 24445 : 24446;
		}

		private int GetDefaultPort(Network network)
		{
			return network == Network.Main ? 23000 :
				network == Network.TestNet ? 23001 : 23002;
		}
	}
}
