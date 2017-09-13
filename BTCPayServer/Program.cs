using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System;
using BTCPayServer.Hosting;
using NBitcoin;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace BTCPayServer
{
	class Program
	{
		static void Main(string[] args)
		{
			ServicePointManager.DefaultConnectionLimit = 100;
			IWebHost host = null;
			try
			{
				var conf = new BTCPayServerOptions();
				conf.LoadArgs(new TextFileConfiguration(args));

				host = new WebHostBuilder()
					.AddPayServer(conf)
					.UseKestrel()
					.UseIISIntegration()
					.UseContentRoot(Directory.GetCurrentDirectory())
					.ConfigureServices(services =>
					{
						services.AddLogging(l =>
						{
							l.AddFilter("Microsoft", LogLevel.Error);
							l.AddProvider(new CustomConsoleLogProvider());
						});
					})
					.UseStartup<Startup>()
					.Build();
				var running = host.RunAsync();
				OpenBrowser(conf.GetUrls().Select(url => url.Replace("0.0.0.0", "127.0.0.1")).First());
				running.GetAwaiter().GetResult();
			}	
			catch(ConfigurationException ex)
			{
				if(!string.IsNullOrEmpty(ex.Message))
					Logs.Configuration.LogError(ex.Message);
			}
			catch(Exception exception)
			{
				Logs.PayServer.LogError("Exception thrown while running the server");
				Logs.PayServer.LogError(exception.ToString());
			}
			finally
			{
				if(host != null)
					host.Dispose();
			}
		}

		public static void OpenBrowser(string url)
		{
			try
			{
				if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					Process.Start(new ProcessStartInfo("cmd", $"/c start {url}")); // Works ok on windows
				}
				else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					Process.Start("xdg-open", url);  // Works ok on linux
				}
				else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					Process.Start("open", url); // Not tested
				}
				else
				{

				}
			}
			catch { }
		}
	}
}
