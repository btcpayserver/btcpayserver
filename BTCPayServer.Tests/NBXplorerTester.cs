using NBitcoin;
using NBitcoin.Tests;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit;

namespace BTCPayServer.Tests
{
	public class NBXplorerTester : IDisposable
	{
		private string _Directory;

		public NBXplorerTester(string scope)
		{
			if(scope == null)
				throw new ArgumentNullException(nameof(scope));
			this._Directory = scope;
		}

		Process _Process;

		public CoreNode Node
		{
			get; set;
		}

		public void Start()
		{
			ProcessLauncher launcher = new ProcessLauncher();
			launcher.GoTo("Repositories", true);
			if(!launcher.GoTo(new[] { "nxbplorer", "NBXplorer" }))
			{
				launcher.Run("git", "clone https://github.com/dgarage/NBXplorer nxbplorer");
				Assert.True(launcher.GoTo(new[] { "nxbplorer", "NBXplorer" }), "Could not clone nxbplorer");
			}
			launcher.PushDirectory();
			if(!launcher.GoTo(new[] { "bin", "Release", "netcoreapp2.0" }) || !launcher.Exists("NBXplorer.dll"))
			{
				launcher.PopDirectory();
				launcher.Run("git", "pull");
				launcher.Run("git", "checkout master");

				launcher.Run("dotnet", "build /p:Configuration=Release");
				Assert.True(launcher.GoTo(new[] { "bin", "Release", "netcoreapp2.0" }), "Could not build NBXplorer");

				launcher.AssertExists("NBXplorer.dll");
			}

			var port = Utils.FreeTcpPort();
			var launcher2 = new ProcessLauncher();
			launcher2.GoTo(_Directory, true);
			launcher2.GoTo("nbxplorer-datadir", true);
			StringBuilder config = new StringBuilder();
			config.AppendLine($"regtest=1");
			config.AppendLine($"port={port}");
			config.AppendLine($"rpc.url={Node.RPCUri.AbsoluteUri}");
			config.AppendLine($"rpc.auth={Node.AuthenticationString}");
			config.AppendLine($"node.endpoint={Node.NodeEndpoint.Address}:{Node.NodeEndpoint.Port}");
			File.WriteAllText(Path.Combine(launcher2.CurrentDirectory, "settings.config"), config.ToString());
			_Process = launcher.Start("dotnet", $"NBXplorer.dll -datadir \"{launcher2.CurrentDirectory}\"");
			ExplorerClient = new NBXplorer.ExplorerClient(Node.Network, new Uri($"http://127.0.0.1:{port}/"));
			CookieFile = Path.Combine(launcher2.CurrentDirectory, ".cookie");
			File.Create(CookieFile).Close(); //Will be wipedout when the client starts
			ExplorerClient.SetCookieFile(CookieFile);
			try
			{
				var cancellationSource = new CancellationTokenSource(10000);
				ExplorerClient.WaitServerStarted(cancellationSource.Token);
			}
			catch(OperationCanceledException)
			{
				Assert.False(_Process.HasExited, "NBXplorer failed to launch");
				throw;
			}
		}

		public NBXplorer.ExplorerClient ExplorerClient
		{
			get; set;
		}
		public string CookieFile
		{
			get;
			set;
		}

		public static NBXplorerTester Create([CallerMemberNameAttribute] string scope = null)
		{
			return new NBXplorerTester(scope);
		}

		public void Dispose()
		{
			if(_Process != null && !_Process.HasExited)
				_Process.Kill();
		}
	}
}
